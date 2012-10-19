﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BEPUphysics;
using BEPUphysics.MathExtensions;
using Microsoft.Xna.Framework;

namespace BEPUphysicsDemos.Demos.Extras.Tests.InverseKinematics
{
    public class SingleBoneAngularMotor : SingleBoneConstraint
    {
        /// <summary>
        /// Gets or sets the target orientation to apply to the target bone.
        /// </summary>
        public Quaternion TargetOrientation;

        protected internal override void UpdateJacobiansAndVelocityBias()
        {
            linearJacobian = new Matrix3X3();
            angularJacobian = Matrix3X3.Identity;

            //Error is in world space. It gets projected onto the jacobians later.
            Quaternion errorQuaternion;
            Quaternion.Conjugate(ref TargetBone.Orientation, out errorQuaternion);
            Quaternion.Multiply(ref TargetOrientation, ref errorQuaternion, out errorQuaternion);
            float angle;
            Vector3 angularError;
            Toolbox.GetAxisAngleFromQuaternion(ref errorQuaternion, out angularError, out angle);
            Vector3.Multiply(ref angularError, angle, out angularError);

            //This is equivalent to projecting the error onto the angular jacobian. The angular jacobian just happens to be the identity matrix!
            Vector3.Multiply(ref angularError, errorCorrectionFactor, out velocityBias);
        }


    }
}