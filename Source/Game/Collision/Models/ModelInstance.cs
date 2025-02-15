﻿/*
 * Copyright (C) 2012-2020 CypherCore <http://github.com/CypherCore>
 * 
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <http://www.gnu.org/licenses/>.
 */

using Framework.Constants;
using Framework.GameMath;
using System;
using System.IO;
using System.Numerics;

namespace Game.Collision
{
    public enum ModelFlags
    {
        M2 = 1,
        HasBound = 1 << 1,
        ParentSpawn = 1 << 2
    }

    public class ModelMinimalData
    {
        public byte flags;
        public byte adtId;
        public uint Id;
        public Vector3 iPos;
        public float iScale;
        public AxisAlignedBox iBound;
        public string name;
    }

    public class ModelSpawn : ModelMinimalData
    {
        public Vector3 iRot;

        public ModelSpawn() { }

        public ModelSpawn(ModelSpawn spawn)
        {
            flags = spawn.flags;
            adtId = spawn.adtId;
            Id = spawn.Id;
            iPos = spawn.iPos;
            iRot = spawn.iRot;
            iScale = spawn.iScale;
            iBound = spawn.iBound;
            name = spawn.name;
        }

        public static bool ReadFromFile(BinaryReader reader, out ModelSpawn spawn)
        {
            spawn = new ModelSpawn();

            spawn.flags = reader.ReadByte();
            spawn.adtId = reader.ReadByte();
            spawn.Id = reader.ReadUInt32();
            spawn.iPos = reader.Read<Vector3>();
            spawn.iRot = reader.Read<Vector3>();
            spawn.iScale = reader.ReadSingle();

            bool has_bound = Convert.ToBoolean(spawn.flags & (uint)ModelFlags.HasBound);
            if (has_bound) // only WMOs have bound in MPQ, only available after computation
            {
                Vector3 bLow = reader.Read<Vector3>();
                Vector3 bHigh = reader.Read<Vector3>();
                spawn.iBound = new AxisAlignedBox(bLow, bHigh);
            }

            uint nameLen = reader.ReadUInt32();
            spawn.name = reader.ReadString((int)nameLen);
            return true;
        }
    }

    public class ModelInstance : ModelMinimalData
    {
        Matrix4x4 iInvRot;
        float iInvScale;
        WorldModel iModel;

        public ModelInstance()
        {
            iInvScale = 0.0f;
            iModel = null;
        }

        public ModelInstance(ModelSpawn spawn, WorldModel model)
        {
            flags = spawn.flags;
            adtId = spawn.adtId;
            Id = spawn.Id;
            iPos = spawn.iPos;
            iScale = spawn.iScale;
            iBound = spawn.iBound;
            name = spawn.name;

            iModel = model;

            Matrix4x4.Invert(Extensions.fromEulerAnglesZYX(MathFunctions.PI * spawn.iRot.Y / 180.0f, MathFunctions.PI * spawn.iRot.X / 180.0f, MathFunctions.PI * spawn.iRot.Z / 180.0f), out iInvRot);

            iInvScale = 1.0f / iScale;
        }

        public bool IntersectRay(Ray pRay, ref float pMaxDist, bool pStopAtFirstHit, ModelIgnoreFlags ignoreFlags)
        {
            if (iModel == null)
                return false;

            float time = pRay.intersectionTime(iBound);
            if (float.IsInfinity(time))
                return false;

            // child bounds are defined in object space:
            Vector3 p = Vector3.Transform((pRay.Origin - iPos) * iInvScale, iInvRot);
            Ray modRay = new Ray(p, Vector3.Transform(pRay.Direction, iInvRot));
            float distance = pMaxDist * iInvScale;
            bool hit = iModel.IntersectRay(modRay, ref distance, pStopAtFirstHit, ignoreFlags);
            if (hit)
            {
                distance *= iScale;
                pMaxDist = distance;
            }
            return hit;
        }

        public void IntersectPoint(Vector3 p, AreaInfo info)
        {
            if (iModel == null)
                return;

            // M2 files don't contain area info, only WMO files
            if (Convert.ToBoolean(flags & (uint)ModelFlags.M2))
                return;
            if (!iBound.contains(p))
                return;
            // child bounds are defined in object space:
            Vector3 pModel = Vector3.Transform((p - iPos) * iInvScale, iInvRot);
            Vector3 zDirModel = Vector3.Transform(new Vector3(0.0f, 0.0f, -1.0f), iInvRot);
            float zDist;
            if (iModel.IntersectPoint(pModel, zDirModel, out zDist, info))
            {
                Vector3 modelGround = pModel + zDist * zDirModel;
                // Transform back to world space. Note that:
                // Mat * vec == vec * Mat.transpose()
                // and for rotation matrices: Mat.inverse() == Mat.transpose()
                float world_Z = ((Vector3.Transform(modelGround, iInvRot)) * iScale + iPos).Z;
                if (info.ground_Z < world_Z)
                {
                    info.ground_Z = world_Z;
                    info.adtId = adtId;
                }
            }
        }

        public bool GetLiquidLevel(Vector3 p, LocationInfo info, ref float liqHeight)
        {
            // child bounds are defined in object space:
            Vector3 pModel = Vector3.Transform((p - iPos) * iInvScale, iInvRot);
            //Vector3 zDirModel = iInvRot * Vector3(0.f, 0.f, -1.f);
            float zDist;
            if (info.hitModel.GetLiquidLevel(pModel, out zDist))
            {
                // calculate world height (zDist in model coords):
                // assume WMO not tilted (wouldn't make much sense anyway)
                liqHeight = zDist * iScale + iPos.Z;
                return true;
            }
            return false;
        }

        public bool GetLocationInfo(Vector3 p, LocationInfo info)
        {
            if (iModel == null)
                return false;

            // M2 files don't contain area info, only WMO files
            if (Convert.ToBoolean(flags & (uint)ModelFlags.M2))
                return false;
            if (!iBound.contains(p))
                return false;
            // child bounds are defined in object space:
            Vector3 pModel = Vector3.Transform((p - iPos) * iInvScale, iInvRot);
            Vector3 zDirModel = Vector3.Transform(new Vector3(0.0f, 0.0f, -1.0f), iInvRot);
            float zDist;
            if (iModel.GetLocationInfo(pModel, zDirModel, out zDist, info))
            {
                Vector3 modelGround = pModel + zDist * zDirModel;
                // Transform back to world space. Note that:
                // Mat * vec == vec * Mat.transpose()
                // and for rotation matrices: Mat.inverse() == Mat.transpose()
                float world_Z = (Vector3.Transform(modelGround, iInvRot) * iScale + iPos).Z;
                if (info.ground_Z < world_Z) // hm...could it be handled automatically with zDist at intersection?
                {
                    info.ground_Z = world_Z;
                    info.hitInstance = this;
                    return true;
                }
            }
            return false;
        }

        public void SetUnloaded() { iModel = null; }
    }
}
