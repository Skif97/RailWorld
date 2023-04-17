using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.MathTools;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace RailWorld
{
    public class RailSection
    {
        public int slotNumberInBLock = 0;
        public Vec3d position;

        public Vec3d centerStartPos;
        public Vec3d centerСenterPos;
        public Vec3d centerEndPos;

        public Vec3d centerStartOffset;
        public Vec3d centerCenterOffset;
        public Vec3d centerEndOffset;

        public Vec3d centerStartTangent;
        public Vec3d centerCenterTangent;
        public Vec3d centerEndTangent;

        public Vec3d centerStartNormal;
        public Vec3d centerCenterNormal;
        public Vec3d centerEndNormal;

        public Vec3d leftStartOffset;
        public Vec3d leftCenterOffset;
        public Vec3d leftEndOffset;

        public Vec3d leftCenterTangent;
        public Vec3d leftCenterNormal;

        public Vec3d rightStartOffset;
        public Vec3d rightCenterOffset;
        public Vec3d rightEndOffset;

        public Vec3d rightCenterTangent;
        public Vec3d rightCenterNormal;

        public double rightLenght;
        public double leftLenght;

        public float leftYaw = 0f;
        public float leftPitch = 0f;
        public float leftRoll = 0f;

        public float rightYaw = 0f;
        public float rightPitch = 0f;
        public float rightRoll = 0f;

        public float centerYaw = 0f;
        public float centerPitch = 0f;
        public float centerRoll = 0f;

        public Vec3d leftScale;
        public Vec3d rightScale;
        public Vec3d centerScale;

        public Vec3d nextSectionBlock;
        public int nextSectionSlot;

        public Vec3d previousSectionBlock;
        public int previousSectionSlot;

        public Vec3d switchOffsetLeft;
        public Vec3d switchOffsetRight;

        public double[] leftMatrix;
        public double[] rightMatrix;
        public double[] centerMatrix;

        public RailSection(ICoreAPI api,  PointOnBezierCurve pointsOnCurveStart, PointOnBezierCurve pointsOnCurveCenter, PointOnBezierCurve pointsOnCurveEnd, double trackWidth)
        {
            double trackRadius = trackWidth / 2;

            centerStartPos = pointsOnCurveStart.position.Clone();
            centerEndPos = pointsOnCurveEnd.position.Clone();

            //centerСenterPos = centerStartPos.AverageCopy(centerEndPos);
            centerСenterPos = pointsOnCurveCenter.position.Clone();

            position = centerСenterPos.FloorCopy();

            centerStartOffset = pointsOnCurveStart.position.SubCopy(position);
            centerStartTangent = pointsOnCurveStart.tangent.Clone();
            centerStartNormal = pointsOnCurveStart.normal.Clone();

            centerEndOffset = pointsOnCurveEnd.position.SubCopy(position);
            centerEndTangent = pointsOnCurveEnd.tangent.Clone();
            centerEndNormal = pointsOnCurveEnd.normal.Clone();

            centerCenterOffset = centerСenterPos.SubCopy(position);
            centerCenterTangent = centerEndOffset.SubCopy(centerStartOffset).Normalize();
            centerCenterNormal = new Vec3d(-centerCenterTangent.Z, 0, centerCenterTangent.X);
            centerCenterNormal.Y = 0f;
            centerCenterNormal.Normalize();

            leftStartOffset = centerStartOffset.AddXZCopy(centerStartNormal.MulCopy(trackRadius));
            rightStartOffset = centerStartOffset.AddXZCopy(centerStartNormal.MulCopy(trackRadius).NegateVec());

            leftEndOffset = centerEndOffset.AddXZCopy(centerEndNormal.MulCopy(trackRadius));
            rightEndOffset = centerEndOffset.AddXZCopy(centerEndNormal.MulCopy(trackRadius).NegateVec());

            leftCenterOffset = leftStartOffset.AverageCopy(leftEndOffset);
            rightCenterOffset = rightStartOffset.AverageCopy(rightEndOffset);

            leftCenterTangent = leftEndOffset.SubCopy(leftStartOffset).Normalize();
            rightCenterTangent = rightEndOffset.SubCopy(rightStartOffset).Normalize();

            leftCenterNormal = new Vec3d(-leftCenterTangent.Z, 0, leftCenterTangent.X);
            leftCenterNormal.Y = 0f;
            leftCenterNormal.Normalize();

            rightCenterNormal = new Vec3d(-rightCenterTangent.Z, 0, rightCenterTangent.X);
            rightCenterNormal.Y = 0f;
            rightCenterNormal.Normalize();

            //дальше производные значения

            leftMatrix = Mat4d.Create();
            rightMatrix = Mat4d.Create();
            centerMatrix = Mat4d.Create();

            leftLenght = leftEndOffset.SubCopy(leftStartOffset).Length() * 1.09f;
            rightLenght = rightEndOffset.SubCopy(rightStartOffset).Length() * 1.09f;

            leftScale = new Vec3d(leftLenght, 1f, 1f);
            rightScale = new Vec3d(rightLenght, 1f, 1f);


            double leftYaw = ModMath.TangentToYaw(leftCenterTangent);
            double rightYaw = ModMath.TangentToYaw(rightCenterTangent);
            double centerYaw = ModMath.TangentToYaw(centerCenterTangent);

            double leftPitch = ModMath.TangentToPitch(leftCenterTangent);
            double rightPitch = ModMath.TangentToPitch(rightCenterTangent);
            double centerPitch = ModMath.TangentToPitch(centerCenterTangent);
            //левая рельса
            double[] quat1 = Quaterniond.Create();
            double[] quat2 = Quaterniond.Create();

            Quaterniond.RotateY(quat1, quat1, leftYaw);
            Quaterniond.SetAxisAngle(quat2, leftCenterNormal.ToDoubleArray(), leftPitch);

            Quaterniond.Multiply(quat1, quat2, quat1);

            Mat4d.FromQuat(leftMatrix, quat1);


            //правая рельса
            quat1 = Quaterniond.Create();
            quat2 = Quaterniond.Create();

            Quaterniond.RotateY(quat1, quat1, rightYaw);
            Quaterniond.SetAxisAngle(quat2, rightCenterNormal.ToDoubleArray(), rightPitch);

            Quaterniond.Multiply(quat1, quat2, quat1);

            Mat4d.FromQuat(rightMatrix, quat1);


            //центр
            quat1 = Quaterniond.Create();
            quat2 = Quaterniond.Create();

            Quaterniond.RotateY(quat1, quat1, centerYaw);
            Quaterniond.SetAxisAngle(quat2, centerCenterNormal.ToDoubleArray(), centerPitch);

            Quaterniond.Multiply(quat1, quat2, quat1);

            Mat4d.FromQuat(centerMatrix, quat1);


            float[] eulerAngles = Quaterniond_Extensions.MatrixToEulerAngles(centerMatrix);

            //float[] eulerAngles = Quaterniond.ToEulerAngles(quat1);
            //float[] eulerAngles = Quaterniond_Extensions.ToEulerAngles(quat1);
            //if (eulerAngles[1] > 0) { }
            this.centerYaw = eulerAngles[0] ;
            //this.centerPitch = 0f;
            //this.centerRoll = 0f;

            this.centerYaw = eulerAngles[1];
            this.centerPitch = eulerAngles[2];
            //this.centerRoll = eulerAngles[0];
            //this.centerYaw = (float)centerYaw;
            //this.centerPitch = (float)centerPitch;
            //this.centerRoll = eulerAngles[2];


            //centerYaw = ModMath.TangentToYaw(centerCenterTangent) + (rnd.NextDouble() / 10.4652f) - 0.0477773956f;



            double rndnum = api.World.Rand.NextDouble() / 2f ;
            centerScale = new Vec3d(1f, 1f, (trackWidth * (1.9f + rndnum)));
            //centerScale = new Vec3d(1f, 1f, (trackWidth * 1.5f));


            nextSectionBlock = position.Clone();
            nextSectionSlot = 0;

            previousSectionBlock = position.Clone();
            previousSectionSlot = 0;

            switchOffsetLeft = new Vec3d(0f, 0f, 0f);
            switchOffsetRight = new Vec3d(0f, 0f, 0f);

        }

        public void FromTreeAttribute(ITreeAttribute tree)
        {
            position = tree.GetVec3d(string.Format("{0}.position", slotNumberInBLock));

            centerStartPos = tree.GetVec3d(string.Format("{0}.centerStartPos", slotNumberInBLock));
            centerСenterPos = tree.GetVec3d(string.Format("{0}.centerСenterPos", slotNumberInBLock));
            centerEndPos = tree.GetVec3d(string.Format("{0}.centerEndPos", slotNumberInBLock));

            centerStartOffset = tree.GetVec3d(string.Format("{0}.centerStartOffset", slotNumberInBLock));
            centerCenterOffset = tree.GetVec3d(string.Format("{0}.centerCenterOffset", slotNumberInBLock));
            centerEndOffset = tree.GetVec3d(string.Format("{0}.centerEndOffset", slotNumberInBLock));

            centerStartTangent = tree.GetVec3d(string.Format("{0}.centerStartTangent", slotNumberInBLock));
            centerCenterTangent = tree.GetVec3d(string.Format("{0}.centerCenterTangent", slotNumberInBLock));
            centerEndTangent = tree.GetVec3d(string.Format("{0}.centerEndTangent", slotNumberInBLock));

            centerStartNormal = tree.GetVec3d(string.Format("{0}.centerStartNormal", slotNumberInBLock));
            centerCenterNormal = tree.GetVec3d(string.Format("{0}.centerCenterNormal", slotNumberInBLock));
            centerEndNormal = tree.GetVec3d(string.Format("{0}.centerEndNormal", slotNumberInBLock));

            leftStartOffset = tree.GetVec3d(string.Format("{0}.leftStartOffset", slotNumberInBLock));
            leftCenterOffset = tree.GetVec3d(string.Format("{0}.leftCenterOffset", slotNumberInBLock));
            leftEndOffset = tree.GetVec3d(string.Format("{0}.leftEndOffset", slotNumberInBLock));

            leftCenterTangent = tree.GetVec3d(string.Format("{0}.leftCenterTangent", slotNumberInBLock));
            leftCenterNormal = tree.GetVec3d(string.Format("{0}.leftCenterNormal", slotNumberInBLock));

            rightStartOffset = tree.GetVec3d(string.Format("{0}.rightStartOffset", slotNumberInBLock));
            rightCenterOffset = tree.GetVec3d(string.Format("{0}.rightCenterOffset", slotNumberInBLock));
            rightEndOffset = tree.GetVec3d(string.Format("{0}.rightEndOffset", slotNumberInBLock));

            rightCenterTangent = tree.GetVec3d(string.Format("{0}.rightCenterTangent", slotNumberInBLock));
            rightCenterNormal = tree.GetVec3d(string.Format("{0}.rightCenterNormal", slotNumberInBLock));

            leftLenght = tree.GetDouble(string.Format("{0}.leftLenght", slotNumberInBLock));
            rightLenght = tree.GetDouble(string.Format("{0}.rightLenght", slotNumberInBLock));


            nextSectionBlock = tree.GetVec3d(string.Format("{0}.nextSectionBlock", slotNumberInBLock));
            nextSectionSlot = tree.GetInt(string.Format("{0}.nextSectionSlot", slotNumberInBLock));

            previousSectionBlock = tree.GetVec3d(string.Format("{0}.previousSectionBlock", slotNumberInBLock));
            previousSectionSlot = tree.GetInt(string.Format("{0}.previousSectionSlot", slotNumberInBLock));

            switchOffsetLeft = tree.GetVec3d(string.Format("{0}.switchOffsetleft", slotNumberInBLock));
            switchOffsetRight = tree.GetVec3d(string.Format("{0}.switchOffsetright", slotNumberInBLock));

            leftPitch = tree.GetFloat(string.Format("{0}.leftPitch", slotNumberInBLock));
            leftYaw = tree.GetFloat(string.Format("{0}.leftYaw", slotNumberInBLock));
            leftRoll = tree.GetFloat(string.Format("{0}.leftRoll", slotNumberInBLock));

            rightPitch = tree.GetFloat(string.Format("{0}.rightPitch", slotNumberInBLock));
            rightYaw = tree.GetFloat(string.Format("{0}.rightYaw", slotNumberInBLock));
            rightRoll = tree.GetFloat(string.Format("{0}.rightRoll", slotNumberInBLock));

            centerPitch = tree.GetFloat(string.Format("{0}.centerPitch", slotNumberInBLock));
            centerYaw = tree.GetFloat(string.Format("{0}.centerYaw", slotNumberInBLock));
            centerRoll = tree.GetFloat(string.Format("{0}.centerRoll", slotNumberInBLock));


            leftScale = tree.GetVec3d(string.Format("{0}.leftScale", slotNumberInBLock));
            rightScale = tree.GetVec3d(string.Format("{0}.rightScale", slotNumberInBLock));
            centerScale = tree.GetVec3d(string.Format("{0}.centerScale", slotNumberInBLock));

            leftMatrix = tree.GetDoubleArray16(string.Format("{0}.leftMatrix", slotNumberInBLock));
            rightMatrix = tree.GetDoubleArray16(string.Format("{0}.rightMatrix", slotNumberInBLock));
            centerMatrix = tree.GetDoubleArray16(string.Format("{0}.centerMatrix", slotNumberInBLock));
        }

        public RailSection(ItemStack itemStack, int slot)
        {
    
            FromTreeAttribute(itemStack.Attributes);
            slotNumberInBLock = slot;
        }

        public RailSection(ITreeAttribute tree, int slot)
        {
            slotNumberInBLock = slot;
            FromTreeAttribute(tree);
        }

        public void UpdateConnections(List<BlockEntityRail> beRailAround)
        {
            foreach (var beRail in beRailAround)
            {
                for (int i = 0; i < beRail.GetRailSections().Count; i++)
                {
                    if (beRail.Pos.ToVec3i() != position.ToVec3i() || i != slotNumberInBLock)
                    {
                        if (beRail.GetRailSections()[i].centerStartPos.Equals(centerEndPos))
                        {
                            nextSectionBlock = beRail.Pos.ToVec3d();
                            nextSectionSlot = i;
                            break;
                            
                        }

                        if (beRail.GetRailSections()[i].centerEndPos.Equals(centerStartPos))
                        {
                            previousSectionBlock = beRail.Pos.ToVec3d();
                            previousSectionSlot = i;
                            break;
                        }
                    }
                }
            }
        }

        public ItemStack ToItemStackAttributes(ItemStack itemStack = null)
        {
            if (itemStack == null) { itemStack = new ItemStack(); }
            ToTreeAttribute(itemStack.Attributes);

            return itemStack;
        }

        public void ToTreeAttribute(ITreeAttribute tree)
        {
            tree.SetVec3d(string.Format("{0}.position", slotNumberInBLock), position);
            tree.SetVec3d(string.Format("{0}.centerStartPos", slotNumberInBLock), centerStartPos);
            tree.SetVec3d(string.Format("{0}.centerСenterPos", slotNumberInBLock), centerСenterPos);
            tree.SetVec3d(string.Format("{0}.centerEndPos", slotNumberInBLock), centerEndPos);

            tree.SetVec3d(string.Format("{0}.centerStartOffset", slotNumberInBLock), centerStartOffset);
            tree.SetVec3d(string.Format("{0}.centerCenterOffset", slotNumberInBLock), centerCenterOffset);
            tree.SetVec3d(string.Format("{0}.centerEndOffset", slotNumberInBLock), centerEndOffset);

            tree.SetVec3d(string.Format("{0}.centerStartTangent", slotNumberInBLock), centerStartTangent);
            tree.SetVec3d(string.Format("{0}.centerCenterTangent", slotNumberInBLock), centerCenterTangent);
            tree.SetVec3d(string.Format("{0}.centerEndTangent", slotNumberInBLock), centerEndTangent);

            tree.SetVec3d(string.Format("{0}.centerStartNormal", slotNumberInBLock), centerStartNormal);
            tree.SetVec3d(string.Format("{0}.centerCenterNormal", slotNumberInBLock), centerCenterNormal);
            tree.SetVec3d(string.Format("{0}.centerEndNormal", slotNumberInBLock), centerEndNormal);

            tree.SetVec3d(string.Format("{0}.leftStartOffset", slotNumberInBLock), leftStartOffset);
            tree.SetVec3d(string.Format("{0}.leftCenterOffset", slotNumberInBLock), leftCenterOffset);
            tree.SetVec3d(string.Format("{0}.leftEndOffset", slotNumberInBLock), leftEndOffset);

            tree.SetVec3d(string.Format("{0}.leftCenterTangent", slotNumberInBLock), leftCenterTangent);
            tree.SetVec3d(string.Format("{0}.leftCenterNormal", slotNumberInBLock), leftCenterNormal);

            tree.SetVec3d(string.Format("{0}.rightStartOffset", slotNumberInBLock), rightStartOffset);
            tree.SetVec3d(string.Format("{0}.rightCenterOffset", slotNumberInBLock), rightCenterOffset);
            tree.SetVec3d(string.Format("{0}.rightEndOffset", slotNumberInBLock), rightEndOffset);

            tree.SetVec3d(string.Format("{0}.rightCenterTangent", slotNumberInBLock), rightCenterTangent);
            tree.SetVec3d(string.Format("{0}.rightCenterNormal", slotNumberInBLock), rightCenterNormal);

            tree.SetDouble(string.Format("{0}.leftLenght", slotNumberInBLock), leftLenght);
            tree.SetDouble(string.Format("{0}.rightLenght", slotNumberInBLock), rightLenght);

            tree.SetVec3d(string.Format("{0}.nextSectionBlock", slotNumberInBLock), nextSectionBlock);
            tree.SetInt(string.Format("{0}.slotAfterStartPos", slotNumberInBLock), nextSectionSlot);

            tree.SetVec3d(string.Format("{0}.previousSectionBlock", slotNumberInBLock), previousSectionBlock);
            tree.SetInt(string.Format("{0}.slotAfterEndPos", slotNumberInBLock), previousSectionSlot);

            tree.SetVec3d(string.Format("{0}.switchOffsetleft", slotNumberInBLock), switchOffsetLeft);
            tree.SetVec3d(string.Format("{0}.switchOffsetright", slotNumberInBLock), switchOffsetRight);

            tree.SetFloat(string.Format("{0}.leftPitch", slotNumberInBLock), leftPitch);
            tree.SetFloat(string.Format("{0}.leftYaw", slotNumberInBLock), leftYaw);
            tree.SetFloat(string.Format("{0}.leftRoll", slotNumberInBLock), leftRoll);

            tree.SetFloat(string.Format("{0}.rightPitch", slotNumberInBLock), rightPitch);
            tree.SetFloat(string.Format("{0}.rightYaw", slotNumberInBLock), rightYaw);
            tree.SetFloat(string.Format("{0}.rightRoll", slotNumberInBLock), rightRoll);

            tree.SetFloat(string.Format("{0}.centerPitch", slotNumberInBLock), centerPitch);
            tree.SetFloat(string.Format("{0}.centerYaw", slotNumberInBLock), centerYaw);
            tree.SetFloat(string.Format("{0}.centerRoll", slotNumberInBLock), centerRoll);

            tree.SetVec3d(string.Format("{0}.leftScale", slotNumberInBLock), leftScale);
            tree.SetVec3d(string.Format("{0}.rightScale", slotNumberInBLock), rightScale);
            tree.SetVec3d(string.Format("{0}.centerScale", slotNumberInBLock), centerScale);

            tree.SetDoubleArray16(string.Format("{0}.leftMatrix", slotNumberInBLock), leftMatrix);
            tree.SetDoubleArray16(string.Format("{0}.rightMatrix", slotNumberInBLock), rightMatrix);
            tree.SetDoubleArray16(string.Format("{0}.centerMatrix", slotNumberInBLock), centerMatrix);

        }
    }
   
}
