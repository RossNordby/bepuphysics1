﻿using System;
using System.Collections.Generic;
using BEPUphysics.BroadPhaseSystems;
using BEPUphysics.Constraints;
using BEPUphysics.NarrowPhaseSystems.Pairs;
using BEPUphysics.SolverSystems;
using BEPUphysics.Threading;
using BEPUphysics.CollisionRuleManagement;
using System.Collections.ObjectModel;
using BEPUphysics.DataStructures;
using System.Diagnostics;

namespace BEPUphysics.NarrowPhaseSystems
{
    ///<summary>
    /// Pair of types.
    ///</summary>
    public struct TypePair : IEquatable<TypePair>
    {
        //Currently this requires some reflective labor.  If perhaps the broad phase entries had some sort of 'id'... and they simply return that int through the interface.
        //Could be 'faster' assuming the supporting logic that creates the ids to begin with isn't too obtuse.
        ///<summary>
        /// First type in the pair.
        ///</summary>
        public Type A;

        ///<summary>
        /// Second type in the pair.
        ///</summary>
        public Type B;

        ///<summary>
        /// Constructs a new type pair.
        ///</summary>
        ///<param name="a">First type in the pair.</param>
        ///<param name="b">Second type in the pair.</param>
        public TypePair(Type a, Type b)
        {
            A = a;
            B = b;
        }

        /// <summary>
        /// Returns the hash code for this instance.
        /// </summary>
        /// <returns>
        /// A 32-bit signed integer that is the hash code for this instance.
        /// </returns>
        /// <filterpriority>2</filterpriority>
        public override int GetHashCode()
        {
            //TODO: Use old hash code system?
            return A.GetHashCode() + B.GetHashCode();
        }


        #region IEquatable<TypePair> Members

        /// <summary>
        /// Indicates whether the current object is equal to another object of the same type.
        /// </summary>
        /// <returns>
        /// true if the current object is equal to the <paramref name="other"/> parameter; otherwise, false.
        /// </returns>
        /// <param name="other">An object to compare with this object.</param>
        public bool Equals(TypePair other)
        {
            return (other.A == A && other.B == B) || (other.B == A && other.A == B);
        }

        #endregion
    }
    ///<summary>
    /// Manages and constructs pair handlers from broad phase overlaps.
    ///</summary>
    public class NarrowPhase : MultithreadedProcessingStage
    {
        RawList<BroadPhaseOverlap> broadPhaseOverlaps;
        ///<summary>
        /// Gets or sets the list of broad phase overlaps used by the narrow phase to manage pairs.
        ///</summary>
        public RawList<BroadPhaseOverlap> BroadPhaseOverlaps { get { return broadPhaseOverlaps; } set { broadPhaseOverlaps = value; } }

        Dictionary<BroadPhaseOverlap, NarrowPhasePair> overlapMapping = new Dictionary<BroadPhaseOverlap, NarrowPhasePair>();
        RawList<NarrowPhasePair> narrowPhasePairs = new RawList<NarrowPhasePair>();
        ///<summary>
        /// Gets the list of Pairs managed by the narrow phase.
        ///</summary>
        public ReadOnlyList<NarrowPhasePair> Pairs
        {
            get
            {
                return new ReadOnlyList<NarrowPhasePair>(narrowPhasePairs);
            }
        }

        ///<summary>
        /// Gets or sets the time step settings used by the narrow phase.
        ///</summary>
        public TimeStepSettings TimeStepSettings { get; set; }

        /// <summary>
        /// Gets or sets the solver into which the narrow phase will put solver updateables generated by narrow phase pairs.
        /// </summary>
        public Solver Solver { get; set; }

        ConcurrentDeque<NarrowPhasePair> newNarrowPhasePairs = new ConcurrentDeque<NarrowPhasePair>();


        ///<summary>
        /// Constructs a new narrow phase.
        ///</summary>
        ///<param name="timeStepSettings">Time step settings used by the narrow phase.</param>
        public NarrowPhase(TimeStepSettings timeStepSettings)
        {
            TimeStepSettings = timeStepSettings;
            updateBroadPhaseOverlapDelegate = UpdateBroadPhaseOverlap;
            Enabled = true;
        }

        ///<summary>
        /// Constructs a new narrow phase.
        ///</summary>
        ///<param name="timeStepSettings">Time step settings used by the narrow phase.</param>
        /// <param name="overlaps">Overlaps list used by the narrow phase to create pairs.</param>
        public NarrowPhase(TimeStepSettings timeStepSettings, RawList<BroadPhaseOverlap> overlaps)
            : this(timeStepSettings)
        {
            broadPhaseOverlaps = overlaps;
        }
        ///<summary>
        /// Constructs a new narrow phase.
        ///</summary>
        ///<param name="timeStepSettings">Time step settings used by the narrow phase.</param>
        /// <param name="overlaps">Overlaps list used by the narrow phase to create pairs.</param>
        /// <param name="threadManager">Thread manager used by the narrow phase.</param>
        public NarrowPhase(TimeStepSettings timeStepSettings, RawList<BroadPhaseOverlap> overlaps, IThreadManager threadManager)
            : this(timeStepSettings, overlaps)
        {
            ThreadManager = threadManager;
            AllowMultithreading = true;
        }

        Action<int> updateBroadPhaseOverlapDelegate;
        void UpdateBroadPhaseOverlap(int i)
        {
            BroadPhaseOverlap overlap = broadPhaseOverlaps.Elements[i];
            if (overlap.collisionRule < CollisionRule.NoNarrowPhasePair)
            {
                NarrowPhasePair narrowPhaseObject;
                //see if the overlap is already present in the narrow phase.
                if (!overlapMapping.TryGetValue(overlap, out narrowPhaseObject))
                {
                    //Create/enqueue based on collision table
                    narrowPhaseObject = NarrowPhaseHelper.GetPair(ref overlap);
                    if (narrowPhaseObject != null)
                    {
                        narrowPhaseObject.NarrowPhase = this;
                        //Add the new object to the 'todo' list.
                        //Technically, this doesn't need to be thread-safe when this is called from the sequential context.
                        //It's just bunched together for maintainability despite the slight performance hit.
                        newNarrowPhasePairs.Enqueue(narrowPhaseObject);
                    }
                }
                if (narrowPhaseObject != null)
                {

                    if (narrowPhaseObject.BroadPhaseOverlap.collisionRule < CollisionRule.NoNarrowPhaseUpdate)
                    {
                        narrowPhaseObject.UpdateCollision(TimeStepSettings.TimeStepDuration);
                    }
                    narrowPhaseObject.NeedsUpdate = false;
                }


            }
        }



        protected override void UpdateMultithreaded()
        {
            ThreadManager.ForLoop(0, broadPhaseOverlaps.Count, updateBroadPhaseOverlapDelegate);

            //Remove stale objects.
            //TODO: This could benefit from a custom data structure (a tiny amount).
            //TODO: This could possibly be done with a computation spreading approach.
            //Problem: Consider a collision pair that has contacts one frame, and in the next, no longer even has a broad phase overlap.
            //It will receive no update, and the collision pair will still have a contact in it.
            //Collision solver will operate on permanently out of date information.
            //One possible solution requires that the user of the narrow phase object checks its age, and if it's out of date, ignore it.
            //In a subsequent frame, the system will get rid of it.  This has an advantage of lowering the (somewhat tiny) per frame cost of removal management.
            //Additionally, in highly chaotic situations where collisions are constantly being created/destroyed, spreading out the computations
            //smooths the work out a bit.


            //TODO:
            //Flush here?
            //Maybe, but not *only* here.
            //Character controller can fiddle with things in a later stage.

            RemoveStaleOverlaps();

            AddNewNarrowPhaseObjects();
        }


        protected override void UpdateSingleThreaded()
        {

            int count = broadPhaseOverlaps.Count;
            for (int i = 0; i < count; i++)
            {
                UpdateBroadPhaseOverlap(i);
            }

            //TODO:
            //Flush here?
            //Maybe, but not *only* here.
            //Character controller can fiddle with things in a later stage.

            RemoveStaleOverlaps();

            AddNewNarrowPhaseObjects();


        }


        void RemoveStaleOverlaps()
        {
            //Remove stale objects.
            for (int i = narrowPhasePairs.count - 1; i >= 0; i--)
            {
                var pair = narrowPhasePairs.Elements[i];

                //A stale overlap is a pair which has not been updated, but not because of inactivity.
                
                //Pairs between two inactive shapes are not updated because the broad phase does not output overlaps
                //between inactive entries.  We need to keep such pairs around, otherwise when they wake up, lots of extra work
                //will be needed and quality will suffer.

                //The classic stale overlap is two objects which have moved apart.  Because the bounding boxes no longer overlap,
                //the broad phase does not generate an overlap for them.  Obviously, we should get rid of such a pair.  
                //Any such pair will have at least one active member.  Having velocity requires activity and teleportation will activate the object.

                //There's another sneaky kind of stale overlap.  Consider a sleeping dynamic object on a Terrain.  The Terrain, being a static object,
                //is considered inactive.  The sleeping dynamic object is also inactive.  Now, remove the sleeping dynamic object.
                //Both objects are still considered inactive.  But the pair is clearly stale- one of its members doesn't even exist anymore!
                //This has nasty side effects, like retaining memory.  To solve this, also check to see if either member does not belong to the simulation.

                if (pair.NeedsUpdate && //If we didn't receive an update in the previous narrow phase run and...
                    (pair.broadPhaseOverlap.entryA.IsActive || pair.broadPhaseOverlap.entryB.IsActive || //one of us is active or..
                    pair.broadPhaseOverlap.entryA.BroadPhase == null || pair.broadPhaseOverlap.entryB.BroadPhase == null)) //one of us doesn't exist anymore...
                {
                    //Get rid of the pair!
                    narrowPhasePairs.FastRemoveAt(i);
                    OnRemovePair(pair);
                }
                else
                    pair.NeedsUpdate = true;

            }



        }

        void AddNewNarrowPhaseObjects()
        {
            //Add new narrow phase objects.  This will typically be a very tiny phase.
            NarrowPhasePair narrowPhaseObject;

            while (newNarrowPhasePairs.TryUnsafeDequeueFirst(out narrowPhaseObject))
            {
                narrowPhasePairs.Add(narrowPhaseObject);
                OnCreatePair(narrowPhaseObject);
            }
        }

        ///<summary>
        /// Gets the pair between two broad phase entries, if any.
        ///</summary>
        ///<param name="entryA">First entry in the pair.</param>
        ///<param name="entryB">Second entry in the pair.</param>
        ///<returns>The pair if it exists, null otherwise.</returns>
        public NarrowPhasePair GetPair(BroadPhaseEntry entryA, BroadPhaseEntry entryB)
        {
            NarrowPhasePair toReturn;
            overlapMapping.TryGetValue(new BroadPhaseOverlap(entryA, entryB), out toReturn);
            return toReturn;
        }

        protected void OnCreatePair(NarrowPhasePair pair)
        {
            overlapMapping.Add(pair.BroadPhaseOverlap, pair);
            pair.OnAddedToNarrowPhase();
            if (CreatingPair != null)
                CreatingPair(pair);
        }
        protected void OnRemovePair(NarrowPhasePair pair)
        {
            overlapMapping.Remove(pair.BroadPhaseOverlap);
            pair.CleanUp();
            pair.Factory.GiveBack(pair);
            if (RemovingPair != null)
                RemovingPair(pair);
        }
        ///<summary>
        /// Fires when the narrow phase creates a pair.
        ///</summary>
        public event Action<NarrowPhasePair> CreatingPair;
        ///<summary>
        /// Fires when the narrow phase removes a pair.
        ///</summary>
        public event Action<NarrowPhasePair> RemovingPair;

        ConcurrentDeque<SolverUpdateableChange> solverUpdateableChanges = new ConcurrentDeque<SolverUpdateableChange>();
        ///<summary>
        /// Enqueues a solver updateable created by some pair for flushing into the solver later.
        ///</summary>
        ///<param name="addedItem">Updateable to add.</param>
        public void NotifyUpdateableAdded(SolverUpdateable addedItem)
        {
            solverUpdateableChanges.Enqueue(new SolverUpdateableChange(true, addedItem));
        }
        ///<summary>
        /// Enqueues a solver updateable removed by some pair for flushing into the solver later.
        ///</summary>
        ///<param name="removedItem">Solver updateable to remove.</param>
        public void NotifyUpdateableRemoved(SolverUpdateable removedItem)
        {
            solverUpdateableChanges.Enqueue(new SolverUpdateableChange(false, removedItem));
        }


        /// <summary>
        /// Flushes the new solver updateables into the solver.
        /// They are 'flux' updateables, so this uses the solver's flux add method.
        /// </summary>
        public void FlushGeneratedSolverUpdateables()
        {
            SolverUpdateableChange change;
            while (solverUpdateableChanges.TryUnsafeDequeueFirst(out change))
            {
                if (change.ShouldAdd)
                {
                    //It is technically possible for a constraint to be added twice, if certain systems interfere.
                    //The character controller is one such system.
                    //We should check the new constraint's solver status before adding it here.
                    if (change.Item.solver == null)
                    {
                        Solver.Add(change.Item);
                    }
                }
                else
                {
                    if (change.Item.solver != null)
                    {
                        Solver.Remove(change.Item);
                    }
                }
            }

        }




    }
}
