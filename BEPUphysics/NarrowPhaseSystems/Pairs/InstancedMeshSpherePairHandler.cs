﻿using System;
using BEPUphysics.BroadPhaseSystems;
using BEPUphysics.BroadPhaseEntries;
using BEPUphysics.BroadPhaseEntries.MobileCollidables;
using BEPUphysics.CollisionTests;
using BEPUphysics.CollisionTests.CollisionAlgorithms.GJK;
using BEPUphysics.CollisionTests.Manifolds;
using BEPUphysics.Constraints.Collision;
using BEPUphysics.DataStructures;
using BEPUphysics.PositionUpdating;
using BEPUphysics.Settings;
using Microsoft.Xna.Framework;
using BEPUphysics.MathExtensions;
using BEPUphysics.ResourceManagement;
using BEPUphysics.CollisionShapes.ConvexShapes;

namespace BEPUphysics.NarrowPhaseSystems.Pairs
{
    ///<summary>
    /// Handles a instanced mesh-convex collision pair.
    ///</summary>
    public class InstancedMeshSpherePairHandler : InstancedMeshPairHandler
    {
        InstancedMeshSphereContactManifold contactManifold = new InstancedMeshSphereContactManifold();
        protected override InstancedMeshContactManifold MeshManifold
        {
            get { return contactManifold; }
        }
        
    }

}
