using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.MathTools;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace TrainWorld
{
    public class RailSection
    {
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

        public double rightlenght;
        public double leftlenght;

        public RailSection(PointOnBezierCurve pointsOnCurveStart, PointOnBezierCurve pointsOnCurveCenter, PointOnBezierCurve pointsOnCurveEnd, double trackWidth)
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
            centerCenterNormal = new Vec3d(-centerCenterTangent.Z, 0, centerCenterTangent.X).Normalize();

            leftStartOffset = centerStartOffset.AddXZCopy(centerStartNormal.MulCopy(trackRadius));
            rightStartOffset = centerStartOffset.AddXZCopy(centerStartNormal.MulCopy(trackRadius).NegateVec());

            leftEndOffset = centerEndOffset.AddXZCopy(centerEndNormal.MulCopy(trackRadius));
            rightEndOffset = centerEndOffset.AddXZCopy(centerEndNormal.MulCopy(trackRadius).NegateVec());

            leftCenterOffset = leftStartOffset.AverageCopy(leftEndOffset);
            rightCenterOffset = rightStartOffset.AverageCopy(rightEndOffset);

            leftCenterTangent = leftEndOffset.SubCopy(leftStartOffset).Normalize();
            rightCenterTangent = rightEndOffset.SubCopy(rightStartOffset).Normalize();

            leftCenterNormal = new Vec3d(-leftCenterTangent.Z, 0, leftCenterTangent.X).Normalize();
            rightCenterNormal = new Vec3d(-rightCenterTangent.Z, 0, rightCenterTangent.X).Normalize();

            leftlenght = leftEndOffset.SubCopy(leftStartOffset).Length() * 1.09f;
            rightlenght = rightEndOffset.SubCopy(rightStartOffset).Length() * 1.09f;
        }

        public void FromTreeAttribute(ITreeAttribute tree, int slot = 0)
        {
            position = tree.GetVec3d(string.Format("{0}.position", slot));

            centerStartPos = tree.GetVec3d(string.Format("{0}.centerStartPos", slot));
            centerСenterPos = tree.GetVec3d(string.Format("{0}.centerСenterPos", slot));
            centerEndPos = tree.GetVec3d(string.Format("{0}.centerEndPos", slot));

            centerStartOffset = tree.GetVec3d(string.Format("{0}.centerStartOffset", slot));
            centerCenterOffset = tree.GetVec3d(string.Format("{0}.centerCenterOffset", slot));
            centerEndOffset = tree.GetVec3d(string.Format("{0}.centerEndOffset", slot));

            centerStartTangent = tree.GetVec3d(string.Format("{0}.centerStartTangent", slot));
            centerCenterTangent = tree.GetVec3d(string.Format("{0}.centerCenterTangent", slot));
            centerEndTangent = tree.GetVec3d(string.Format("{0}.centerEndTangent", slot));

            centerStartNormal = tree.GetVec3d(string.Format("{0}.centerStartNormal", slot));
            centerCenterNormal = tree.GetVec3d(string.Format("{0}.centerCenterNormal", slot));
            centerEndNormal = tree.GetVec3d(string.Format("{0}.centerEndNormal", slot));

            leftStartOffset = tree.GetVec3d(string.Format("{0}.leftStartOffset", slot));
            leftCenterOffset = tree.GetVec3d(string.Format("{0}.leftCenterOffset", slot));
            leftEndOffset = tree.GetVec3d(string.Format("{0}.leftEndOffset", slot));

            leftCenterTangent = tree.GetVec3d(string.Format("{0}.leftCenterTangent", slot));
            leftCenterNormal = tree.GetVec3d(string.Format("{0}.leftCenterNormal", slot));

            rightStartOffset = tree.GetVec3d(string.Format("{0}.rightStartOffset", slot));
            rightCenterOffset = tree.GetVec3d(string.Format("{0}.rightCenterOffset", slot));
            rightEndOffset = tree.GetVec3d(string.Format("{0}.rightEndOffset", slot));

            rightCenterTangent = tree.GetVec3d(string.Format("{0}.rightCenterTangent", slot));
            rightCenterNormal = tree.GetVec3d(string.Format("{0}.rightCenterNormal", slot));

            leftlenght = tree.GetDouble(string.Format("{0}.leftlenght", slot));
            rightlenght = tree.GetDouble(string.Format("{0}.rightlenght", slot));
        }

        public RailSection(ItemStack itemStack , int slot = 0)
        {
            FromTreeAttribute(itemStack.Attributes, slot);
        }

        public RailSection(ITreeAttribute tree, int slot = 0)
        {
            FromTreeAttribute(tree, slot);
        }


        public ItemStack ToItemStackAttributes(ItemStack itemStack = null)
        {
            if (itemStack == null) { itemStack = new ItemStack(); }
            ToTreeAttribute(itemStack.Attributes);

            return itemStack;
        }

        public void ToTreeAttribute(ITreeAttribute tree, int slot = 0)
        {
            tree.SetVec3d(string.Format("{0}.position", slot), position);
            tree.SetVec3d(string.Format("{0}.centerStartPos", slot), centerStartPos);
            tree.SetVec3d(string.Format("{0}.centerСenterPos", slot), centerСenterPos);
            tree.SetVec3d(string.Format("{0}.centerEndPos", slot), centerEndPos);

            tree.SetVec3d(string.Format("{0}.centerStartOffset", slot), centerStartOffset);
            tree.SetVec3d(string.Format("{0}.centerCenterOffset", slot), centerCenterOffset);
            tree.SetVec3d(string.Format("{0}.centerEndOffset", slot), centerEndOffset);

            tree.SetVec3d(string.Format("{0}.centerStartTangent", slot), centerStartTangent);
            tree.SetVec3d(string.Format("{0}.centerCenterTangent", slot), centerCenterTangent);
            tree.SetVec3d(string.Format("{0}.centerEndTangent", slot), centerEndTangent);

            tree.SetVec3d(string.Format("{0}.centerStartNormal", slot), centerStartNormal);
            tree.SetVec3d(string.Format("{0}.centerCenterNormal", slot), centerCenterNormal);
            tree.SetVec3d(string.Format("{0}.centerEndNormal", slot), centerEndNormal);

            tree.SetVec3d(string.Format("{0}.leftStartOffset", slot), leftStartOffset);
            tree.SetVec3d(string.Format("{0}.leftCenterOffset", slot), leftCenterOffset);
            tree.SetVec3d(string.Format("{0}.leftEndOffset", slot), leftEndOffset);

            tree.SetVec3d(string.Format("{0}.leftCenterTangent", slot), leftCenterTangent);
            tree.SetVec3d(string.Format("{0}.leftCenterNormal", slot), leftCenterNormal);

            tree.SetVec3d(string.Format("{0}.rightStartOffset", slot), rightStartOffset);
            tree.SetVec3d(string.Format("{0}.rightCenterOffset", slot), rightCenterOffset);
            tree.SetVec3d(string.Format("{0}.rightEndOffset", slot), rightEndOffset);

            tree.SetVec3d(string.Format("{0}.rightCenterTangent", slot), rightCenterTangent);
            tree.SetVec3d(string.Format("{0}.rightCenterNormal", slot), rightCenterNormal);

            tree.SetDouble(string.Format("{0}.leftlenght", slot), leftlenght);
            tree.SetDouble(string.Format("{0}.rightlenght", slot), rightlenght);

        }
    }
   
}
