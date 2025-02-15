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
using Framework.Database;
using Framework.IO;
using Game.DataStorage;
using Game.Entities;
using Game.Maps;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Game.Chat
{
    [CommandGroup("gobject", RBACPermissions.CommandGobject)]
    class GameObjectCommands
    {
        [Command("activate", RBACPermissions.CommandGobjectActivate)]
        static bool HandleGameObjectActivateCommand(StringArguments args, CommandHandler handler)
        {
            if (args.Empty())
                return false;

            string id = handler.ExtractKeyFromLink(args, "Hgameobject");
            if (string.IsNullOrEmpty(id))
                return false;

            if (!ulong.TryParse(id, out ulong guidLow) || guidLow == 0)
                return false;

            GameObject obj = handler.GetObjectFromPlayerMapByDbGuid(guidLow);
            if (!obj)
            {
                handler.SendSysMessage(CypherStrings.CommandObjnotfound, guidLow);
                return false;
            }

            uint autoCloseTime = obj.GetGoInfo().GetAutoCloseTime() != 0 ? 10000u : 0u;

            // Activate
            obj.SetLootState(LootState.Ready);
            obj.UseDoorOrButton(autoCloseTime, false, handler.GetSession().GetPlayer());

            handler.SendSysMessage("Object activated!");
            return true;
        }

        [Command("delete", RBACPermissions.CommandGobjectDelete)]
        static bool HandleGameObjectDeleteCommand(StringArguments args, CommandHandler handler)
        {
            // number or [name] Shift-click form |color|Hgameobject:go_guid|h[name]|h|r
            string id = handler.ExtractKeyFromLink(args, "Hgameobject");
            if (string.IsNullOrEmpty(id))
                return false;

            if (!ulong.TryParse(id, out ulong spawnId) || spawnId == 0)
                return false;

            GameObject obj = handler.GetObjectFromPlayerMapByDbGuid(spawnId);
            if (obj != null)
            {
                Player player = handler.GetSession().GetPlayer();
                ObjectGuid ownerGuid = obj.GetOwnerGUID();
                if (!ownerGuid.IsEmpty())
                {
                    Unit owner = Global.ObjAccessor.GetUnit(player, ownerGuid);
                    if (!owner || !ownerGuid.IsPlayer())
                    {
                        handler.SendSysMessage(CypherStrings.CommandDelobjrefercreature, ownerGuid.ToString(), obj.GetGUID().ToString());
                        return false;
                    }

                    owner.RemoveGameObject(obj, false);
                }
            }

            if (GameObject.DeleteFromDB(spawnId))
            {
                handler.SendSysMessage(CypherStrings.CommandDelobjmessage, spawnId);
                return true;
            }

            handler.SendSysMessage(CypherStrings.CommandObjnotfound, obj.GetGUID().ToString());
            return false;
        }

        [Command("despawngroup", RBACPermissions.CommandGobjectDespawngroup)]
        static bool HandleGameObjectDespawnGroup(StringArguments args, CommandHandler handler)
        {
            if (args.Empty())
                return false;

            bool deleteRespawnTimes = false;
            uint groupId = 0;

            // Decode arguments
            string arg = args.NextString();
            while (!arg.IsEmpty())
            {
                string thisArg = arg.ToLower();
                if (thisArg == "removerespawntime")
                    deleteRespawnTimes = true;
                else if (thisArg.IsEmpty() || !thisArg.IsNumber())
                    return false;
                else
                    groupId = uint.Parse(thisArg);

                arg = args.NextString();
            }

            Player player = handler.GetSession().GetPlayer();

            if (!player.GetMap().SpawnGroupDespawn(groupId, deleteRespawnTimes))
            {
                handler.SendSysMessage(CypherStrings.SpawngroupBadgroup, groupId);
                return false;
            }

            return true;
        }

        [Command("info", RBACPermissions.CommandGobjectInfo)]
        static bool HandleGameObjectInfoCommand(StringArguments args, CommandHandler handler)
        {
            if (args.Empty())
                return false;

            string param1 = handler.ExtractKeyFromLink(args, "Hgameobject_entry");
            if (param1.IsEmpty())
                return false;

            GameObject thisGO = null;
            GameObjectData data = null;

            uint entry;
            ulong spawnId = 0;
            if (param1.Equals("guid"))
            {
                string cValue = handler.ExtractKeyFromLink(args, "Hgameobject");
                if (cValue.IsEmpty())
                    return false;

                if (!ulong.TryParse(cValue, out spawnId))
                    return false;

                data = Global.ObjectMgr.GetGameObjectData(spawnId);
                if (data == null)
                {
                    handler.SendSysMessage(CypherStrings.CommandObjnotfound, spawnId);
                    return false;
                }

                entry = data.Id;
                thisGO = handler.GetObjectFromPlayerMapByDbGuid(spawnId);
            }
            else
            {
                if (!uint.TryParse(param1, out entry))
                    return false;
            }

            GameObjectTemplate gameObjectInfo = Global.ObjectMgr.GetGameObjectTemplate(entry);
            if (gameObjectInfo == null)
            {
                handler.SendSysMessage(CypherStrings.GameobjectNotExist, entry);
                return false;
            }

            GameObjectTypes type = gameObjectInfo.type;
            uint displayId = gameObjectInfo.displayId;
            string name = gameObjectInfo.name;
            uint lootId = gameObjectInfo.GetLootId();

            // If we have a real object, send some info about it
            if (thisGO != null)
            {
                handler.SendSysMessage(CypherStrings.SpawninfoGuidinfo, thisGO.GetGUID().ToString());
                handler.SendSysMessage(CypherStrings.SpawninfoCompatibilityMode, thisGO.GetRespawnCompatibilityMode());

                if (thisGO.GetGameObjectData() != null && thisGO.GetGameObjectData().spawnGroupData.groupId != 0)
                {
                    SpawnGroupTemplateData groupData = thisGO.ToGameObject().GetGameObjectData().spawnGroupData;
                    handler.SendSysMessage(CypherStrings.SpawninfoGroupId, groupData.name, groupData.groupId, groupData.flags, thisGO.GetMap().IsSpawnGroupActive(groupData.groupId));
                }

                GameObjectOverride goOverride = Global.ObjectMgr.GetGameObjectOverride(spawnId);
                if (goOverride == null)
                    goOverride = Global.ObjectMgr.GetGameObjectTemplateAddon(entry);
                if (goOverride != null)
                    handler.SendSysMessage(CypherStrings.GoinfoAddon, goOverride.Faction, goOverride.Flags);
            }

            if (data != null)
            {
                data.rotation.toEulerAnglesZYX(out float yaw, out float pitch, out float roll);
                handler.SendSysMessage(CypherStrings.SpawninfoSpawnidLocation, data.SpawnId, data.SpawnPoint.GetPositionX(), data.SpawnPoint.GetPositionY(), data.SpawnPoint.GetPositionZ());
                handler.SendSysMessage(CypherStrings.SpawninfoRotation, yaw, pitch, roll);
            }

            handler.SendSysMessage(CypherStrings.GoinfoEntry, entry);
            handler.SendSysMessage(CypherStrings.GoinfoType, type);
            handler.SendSysMessage(CypherStrings.GoinfoLootid, lootId);
            handler.SendSysMessage(CypherStrings.GoinfoDisplayid, displayId);
            handler.SendSysMessage(CypherStrings.GoinfoName, name);
            handler.SendSysMessage(CypherStrings.GoinfoSize, gameObjectInfo.size);

            handler.SendSysMessage(CypherStrings.ObjectInfoAIInfo, gameObjectInfo.AIName, Global.ObjectMgr.GetScriptName(gameObjectInfo.ScriptId));
            var ai = thisGO != null ? thisGO.GetAI() : null;
            if (ai != null)
                handler.SendSysMessage(CypherStrings.ObjectInfoAIType, nameof(ai));

            GameObjectDisplayInfoRecord modelInfo = CliDB.GameObjectDisplayInfoStorage.LookupByKey(displayId);
            if (modelInfo != null)
                handler.SendSysMessage(CypherStrings.GoinfoModel, modelInfo.GeoBoxMax.X, modelInfo.GeoBoxMax.Y, modelInfo.GeoBoxMax.Z, modelInfo.GeoBoxMin.X, modelInfo.GeoBoxMin.Y, modelInfo.GeoBoxMin.Z);

            return true;
        }

        [Command("move", RBACPermissions.CommandGobjectMove)]
        static bool HandleGameObjectMoveCommand(StringArguments args, CommandHandler handler)
        {
            // number or [name] Shift-click form |color|Hgameobject:go_guid|h[name]|h|r
            string id = handler.ExtractKeyFromLink(args, "Hgameobject");
            if (string.IsNullOrEmpty(id))
                return false;

            if (!ulong.TryParse(id, out ulong guidLow) || guidLow == 0)
                return false;

            GameObject obj = handler.GetObjectFromPlayerMapByDbGuid(guidLow);
            if (!obj)
            {
                handler.SendSysMessage(CypherStrings.CommandObjnotfound, guidLow);
                return false;
            }

            string toX = args.NextString();
            string toY = args.NextString();
            string toZ = args.NextString();

            float x, y, z;
            if (string.IsNullOrEmpty(toX))
            {
                Player player = handler.GetSession().GetPlayer();
                player.GetPosition(out x, out y, out z);
            }
            else
            {
                if (!float.TryParse(toX, out x))
                    return false;

                if (!float.TryParse(toY, out y))
                    return false;

                if (!float.TryParse(toZ, out z))
                    return false;

                if (!GridDefines.IsValidMapCoord(obj.GetMapId(), x, y, z))
                {
                    handler.SendSysMessage(CypherStrings.InvalidTargetCoord, x, y, obj.GetMapId());
                    return false;
                }
            }

            Map map = obj.GetMap();
            obj.Relocate(x, y, z, obj.GetOrientation());

            // update which cell has this gameobject registered for loading
            Global.ObjectMgr.RemoveGameObjectFromGrid(guidLow, obj.GetGameObjectData());
            obj.SaveToDB();
            Global.ObjectMgr.AddGameObjectToGrid(guidLow, obj.GetGameObjectData());

            // Generate a completely new spawn with new guid
            // client caches recently deleted objects and brings them back to life
            // when CreateObject block for this guid is received again
            // however it entirely skips parsing that block and only uses already known location
            obj.Delete();

            obj = GameObject.CreateGameObjectFromDB(guidLow, map);
            if (!obj)
                return false;

            handler.SendSysMessage(CypherStrings.CommandMoveobjmessage, obj.GetSpawnId(), obj.GetGoInfo().name, obj.GetGUID().ToString());

            return true;
        }

        [Command("near", RBACPermissions.CommandGobjectNear)]
        static bool HandleGameObjectNearCommand(StringArguments args, CommandHandler handler)
        {
            float distance = args.Empty() ? 10.0f : args.NextSingle();
            uint count = 0;

            Player player = handler.GetPlayer();

            PreparedStatement stmt = DB.World.GetPreparedStatement(WorldStatements.SEL_GAMEOBJECT_NEAREST);
            stmt.AddValue(0, player.GetPositionX());
            stmt.AddValue(1, player.GetPositionY());
            stmt.AddValue(2, player.GetPositionZ());
            stmt.AddValue(3, player.GetMapId());
            stmt.AddValue(4, player.GetPositionX());
            stmt.AddValue(5, player.GetPositionY());
            stmt.AddValue(6, player.GetPositionZ());
            stmt.AddValue(7, distance * distance);
            SQLResult result = DB.World.Query(stmt);

            if (!result.IsEmpty())
            {
                do
                {
                    ulong guid = result.Read<ulong>(0);
                    uint entry = result.Read<uint>(1);
                    float x = result.Read<float>(2);
                    float y = result.Read<float>(3);
                    float z = result.Read<float>(4);
                    ushort mapId = result.Read<ushort>(5);

                    GameObjectTemplate gameObjectInfo = Global.ObjectMgr.GetGameObjectTemplate(entry);
                    if (gameObjectInfo == null)
                        continue;

                    handler.SendSysMessage(CypherStrings.GoListChat, guid, entry, guid, gameObjectInfo.name, x, y, z, mapId, "", "");

                    ++count;
                } while (result.NextRow());
            }

            handler.SendSysMessage(CypherStrings.CommandNearobjmessage, distance, count);
            return true;
        }

        [Command("spawngroup", RBACPermissions.CommandGobjectSpawngroup)]
        static bool HandleGameObjectSpawnGroup(StringArguments args, CommandHandler handler)
        {
            if (args.Empty())
                return false;

            bool ignoreRespawn = false;
            bool force = false;
            uint groupId = 0;

            // Decode arguments
            string arg = args.NextString();
            while (!arg.IsEmpty())
            {
                string thisArg = arg.ToLower();
                if (thisArg == "ignorerespawn")
                    ignoreRespawn = true;
                else if (thisArg == "force")
                    force = true;
                else if (thisArg.IsEmpty() || !thisArg.IsNumber())
                    return false;
                else
                    groupId = uint.Parse(thisArg);

                arg = args.NextString();
            }

            Player player = handler.GetSession().GetPlayer();

            List<WorldObject> creatureList = new();
            if (!player.GetMap().SpawnGroupSpawn(groupId, ignoreRespawn, force, creatureList))
            {
                handler.SendSysMessage(CypherStrings.SpawngroupBadgroup, groupId);
                return false;
            }

            handler.SendSysMessage(CypherStrings.SpawngroupSpawncount, creatureList.Count);
            foreach (WorldObject obj in creatureList)
                handler.SendSysMessage($"{obj.GetName()} ({obj.GetGUID()})");

            return true;
        }

        [Command("target", RBACPermissions.CommandGobjectTarget)]
        static bool HandleGameObjectTargetCommand(StringArguments args, CommandHandler handler)
        {
            Player player = handler.GetSession().GetPlayer();
            SQLResult result;
            var activeEventsList = Global.GameEventMgr.GetActiveEventList();

            if (!args.Empty())
            {
                // number or [name] Shift-click form |color|Hgameobject_entry:go_id|h[name]|h|r
                string idStr = handler.ExtractKeyFromLink(args, "Hgameobject_entry");
                if (string.IsNullOrEmpty(idStr))
                    return false;

                if (!uint.TryParse(idStr, out uint objectId) || objectId != 0)
                    result = DB.World.Query("SELECT guid, id, position_x, position_y, position_z, orientation, map, PhaseId, PhaseGroup, (POW(position_x - '{0}', 2) + POW(position_y - '{1}', 2) + POW(position_z - '{2}', 2)) AS order_ FROM gameobject WHERE map = '{3}' AND id = '{4}' ORDER BY order_ ASC LIMIT 1",
                    player.GetPositionX(), player.GetPositionY(), player.GetPositionZ(), player.GetMapId(), objectId);
                else
                {
                    result = DB.World.Query(
                        "SELECT guid, id, position_x, position_y, position_z, orientation, map, PhaseId, PhaseGroup, (POW(position_x - {0}, 2) + POW(position_y - {1}, 2) + POW(position_z - {2}, 2)) AS order_ " +
                        "FROM gameobject LEFT JOIN gameobject_template ON gameobject_template.entry = gameobject.id WHERE map = {3} AND name LIKE CONCAT('%%', '{4}', '%%') ORDER BY order_ ASC LIMIT 1",
                        player.GetPositionX(), player.GetPositionY(), player.GetPositionZ(), player.GetMapId(), objectId);
                }
            }
            else
            {
                StringBuilder eventFilter = new();
                eventFilter.Append(" AND (eventEntry IS NULL ");
                bool initString = true;

                foreach (var entry in activeEventsList)
                {
                    if (initString)
                    {
                        eventFilter.Append("OR eventEntry IN (" + entry);
                        initString = false;
                    }
                    else
                        eventFilter.Append(',' + entry);
                }

                if (!initString)
                    eventFilter.Append("))");
                else
                    eventFilter.Append(')');

                result = DB.World.Query("SELECT gameobject.guid, id, position_x, position_y, position_z, orientation, map, PhaseId, PhaseGroup, " +
                    "(POW(position_x - {0}, 2) + POW(position_y - {1}, 2) + POW(position_z - {2}, 2)) AS order_ FROM gameobject " +
                    "LEFT OUTER JOIN game_event_gameobject on gameobject.guid = game_event_gameobject.guid WHERE map = '{3}' {4} ORDER BY order_ ASC LIMIT 10",
                    handler.GetSession().GetPlayer().GetPositionX(), handler.GetSession().GetPlayer().GetPositionY(), handler.GetSession().GetPlayer().GetPositionZ(),
                    handler.GetSession().GetPlayer().GetMapId(), eventFilter.ToString());
            }

            if (result.IsEmpty())
            {
                handler.SendSysMessage(CypherStrings.CommandTargetobjnotfound);
                return true;
            }

            bool found = false;
            float x, y, z, o;
            ulong guidLow;
            uint id, phaseId, phaseGroup;
            ushort mapId;
            uint poolId;

            do
            {
                guidLow = result.Read<ulong>(0);
                id = result.Read<uint>(1);
                x = result.Read<float>(2);
                y = result.Read<float>(3);
                z = result.Read<float>(4);
                o = result.Read<float>(5);
                mapId = result.Read<ushort>(6);
                phaseId = result.Read<uint>(7);
                phaseGroup = result.Read<uint>(8);
                poolId = Global.PoolMgr.IsPartOfAPool<GameObject>(guidLow);
                if (poolId == 0 || Global.PoolMgr.IsSpawnedObject<GameObject>(guidLow))
                    found = true;
            } while (result.NextRow() && !found);

            if (!found)
            {
                handler.SendSysMessage(CypherStrings.GameobjectNotExist, id);
                return false;
            }

            GameObjectTemplate objectInfo = Global.ObjectMgr.GetGameObjectTemplate(id);

            if (objectInfo == null)
            {
                handler.SendSysMessage(CypherStrings.GameobjectNotExist, id);
                return false;
            }

            GameObject target = handler.GetObjectFromPlayerMapByDbGuid(guidLow);

            handler.SendSysMessage(CypherStrings.GameobjectDetail, guidLow, objectInfo.name, guidLow, id, x, y, z, mapId, o, phaseId, phaseGroup);

            if (target)
            {
                int curRespawnDelay = (int)(target.GetRespawnTimeEx() - GameTime.GetGameTime());
                if (curRespawnDelay < 0)
                    curRespawnDelay = 0;

                string curRespawnDelayStr = Time.secsToTimeString((uint)curRespawnDelay, TimeFormat.ShortText);
                string defRespawnDelayStr = Time.secsToTimeString(target.GetRespawnDelay(), TimeFormat.ShortText);

                handler.SendSysMessage(CypherStrings.CommandRawpawntimes, defRespawnDelayStr, curRespawnDelayStr);
            }
            return true;
        }

        [Command("turn", RBACPermissions.CommandGobjectTurn)]
        static bool HandleGameObjectTurnCommand(StringArguments args, CommandHandler handler)
        {
            // number or [name] Shift-click form |color|Hgameobject:go_id|h[name]|h|r
            string id = handler.ExtractKeyFromLink(args, "Hgameobject");
            if (string.IsNullOrEmpty(id))
                return false;

            if (!ulong.TryParse(id, out ulong guidLow) || guidLow == 0)
                return false;

            GameObject obj = handler.GetObjectFromPlayerMapByDbGuid(guidLow);
            if (!obj)
            {
                handler.SendSysMessage(CypherStrings.CommandObjnotfound, guidLow);
                return false;
            }

            string orientation = args.NextString();
            float oz = 0.0f, oy = 0.0f, ox = 0.0f;
            if (!orientation.IsEmpty())
            {
                if (!float.TryParse(orientation, out oz))
                    return false;

                orientation = args.NextString();
                if (!orientation.IsEmpty())
                {
                    if (!float.TryParse(orientation, out oy))
                        return false;

                    orientation = args.NextString();
                    if (!orientation.IsEmpty())
                    {
                        if (!float.TryParse(orientation, out ox))
                            return false;
                    }
                }
            }
            else
            {
                Player player = handler.GetPlayer();
                oz = player.GetOrientation();
            }

            Map map = obj.GetMap();

            obj.Relocate(obj.GetPositionX(), obj.GetPositionY(), obj.GetPositionZ());
            obj.SetLocalRotationAngles(oz, oy, ox);
            obj.SaveToDB();

            // Generate a completely new spawn with new guid
            // client caches recently deleted objects and brings them back to life
            // when CreateObject block for this guid is received again
            // however it entirely skips parsing that block and only uses already known location
            obj.Delete();

            obj = GameObject.CreateGameObjectFromDB(guidLow, map);
            if (!obj)
                return false;

            handler.SendSysMessage(CypherStrings.CommandTurnobjmessage, obj.GetSpawnId(), obj.GetGoInfo().name, obj.GetGUID().ToString(), obj.GetOrientation());

            return true;
        }

        [CommandGroup("add", RBACPermissions.CommandGobjectAdd)]
        class AddCommands
        {
            [Command("", RBACPermissions.CommandGobjectAdd)]
            static bool HandleGameObjectAddCommand(StringArguments args, CommandHandler handler)
            {
                if (args.Empty())
                    return false;

                // number or [name] Shift-click form |color|Hgameobject_entry:go_id|h[name]|h|r
                string idStr = handler.ExtractKeyFromLink(args, "Hgameobject_entry");
                if (string.IsNullOrEmpty(idStr))
                    return false;

                if (!uint.TryParse(idStr, out uint objectId) || objectId == 0)
                    return false;

                uint spawntimeSecs = args.NextUInt32();

                GameObjectTemplate objectInfo = Global.ObjectMgr.GetGameObjectTemplate(objectId);
                if (objectInfo == null)
                {
                    handler.SendSysMessage(CypherStrings.GameobjectNotExist, objectId);
                    return false;
                }

                if (objectInfo.displayId != 0 && !CliDB.GameObjectDisplayInfoStorage.ContainsKey(objectInfo.displayId))
                {
                    // report to DB errors log as in loading case
                    Log.outError(LogFilter.Sql, "Gameobject (Entry {0} GoType: {1}) have invalid displayId ({2}), not spawned.", objectId, objectInfo.type, objectInfo.displayId);
                    handler.SendSysMessage(CypherStrings.GameobjectHaveInvalidData, objectId);
                    return false;
                }

                Player player = handler.GetPlayer();
                Map map = player.GetMap();

                GameObject obj = GameObject.CreateGameObject(objectInfo.entry, map, player, Quaternion.CreateFromRotationMatrix(Extensions.fromEulerAnglesZYX(player.GetOrientation(), 0.0f, 0.0f)), 255, GameObjectState.Ready);
                if (!obj)
                    return false;

                PhasingHandler.InheritPhaseShift(obj, player);

                if (spawntimeSecs != 0)
                {
                    obj.SetRespawnTime((int)spawntimeSecs);
                }

                // fill the gameobject data and save to the db
                obj.SaveToDB(map.GetId(), new List<Difficulty>() { map.GetDifficultyID() });
                ulong spawnId = obj.GetSpawnId();

                // this will generate a new guid if the object is in an instance
                obj = GameObject.CreateGameObjectFromDB(spawnId, map);
                if (!obj)
                    return false;

                // TODO: is it really necessary to add both the real and DB table guid here ?
                Global.ObjectMgr.AddGameObjectToGrid(spawnId, Global.ObjectMgr.GetGameObjectData(spawnId));
                handler.SendSysMessage(CypherStrings.GameobjectAdd, objectId, objectInfo.name, spawnId, player.GetPositionX(), player.GetPositionY(), player.GetPositionZ());
                return true;
            }

            [Command("temp", RBACPermissions.CommandGobjectAddTemp)]
            static bool HandleGameObjectAddTempCommand(StringArguments args, CommandHandler handler)
            {
                if (args.Empty())
                    return false;

                uint id = args.NextUInt32();
                if (id == 0)
                    return false;

                Player player = handler.GetPlayer();

                uint spawntime = args.NextUInt32();
                uint spawntm = 300;

                if (spawntime != 0)
                    spawntm = spawntime;

                Quaternion rotation = Quaternion.CreateFromRotationMatrix(Extensions.fromEulerAnglesZYX(player.GetOrientation(), 0.0f, 0.0f));
                if (Global.ObjectMgr.GetGameObjectTemplate(id) == null)
                {
                    handler.SendSysMessage(CypherStrings.GameobjectNotExist, id);
                    return false;
                }

                player.SummonGameObject(id, player, rotation, spawntm);

                return true;
            }
        }

        [CommandGroup("set", RBACPermissions.CommandGobjectSet)]
        class SetCommands
        {
            [Command("phase", RBACPermissions.CommandGobjectSetPhase)]
            static bool HandleGameObjectSetPhaseCommand(StringArguments args, CommandHandler handler)
            {
                /*// number or [name] Shift-click form |color|Hgameobject:go_id|h[name]|h|r
                string id = handler.extractKeyFromLink(args, "Hgameobject");
                if (string.IsNullOrEmpty(id))
                    return false;

                ulong guidLow = ulong.Parse(id);
                if (guidLow == 0)
                    return false;

                GameObject obj = null;

                // by DB guid
                GameObjectData gameObjectData = Global.ObjectMgr.GetGOData(guidLow);
                if (gameObjectData != null)
                    obj = handler.GetObjectGlobalyWithGuidOrNearWithDbGuid(guidLow, gameObjectData.id);

                if (!obj)
                {
                    handler.SendSysMessage(CypherStrings.CommandObjnotfound, guidLow);
                    return false;
                }

                uint phaseMask = args.NextUInt32();
                if (phaseMask == 0)
                {
                    handler.SendSysMessage(CypherStrings.BadValue);
                    return false;
                }

                obj.SetPhaseMask(phaseMask, true);
                obj.SaveToDB();*/
                return true;
            }

            [Command("state", RBACPermissions.CommandGobjectSetState)]
            static bool HandleGameObjectSetStateCommand(StringArguments args, CommandHandler handler)
            {
                // number or [name] Shift-click form |color|Hgameobject:go_id|h[name]|h|r
                string id = handler.ExtractKeyFromLink(args, "Hgameobject");
                if (string.IsNullOrEmpty(id))
                    return false;

                if (!ulong.TryParse(id, out ulong guidLow) || guidLow == 0)
                    return false;

                GameObject obj = handler.GetObjectFromPlayerMapByDbGuid(guidLow);
                if (!obj)
                {
                    handler.SendSysMessage(CypherStrings.CommandObjnotfound, guidLow);
                    return false;
                }

                string type = args.NextString();
                if (string.IsNullOrEmpty(type))
                    return false;

                if (!int.TryParse(type, out int objectType))
                    return false;

                if (objectType < 0)
                {
                    if (objectType == -1)
                        obj.SendGameObjectDespawn();
                    else if (objectType == -2)
                        return false;
                    return true;
                }

                string state = args.NextString();
                if (string.IsNullOrEmpty(state))
                    return false;

                if (!uint.TryParse(state, out uint objectState))
                    return false;

                switch (objectType)
                {
                    case 0:
                        obj.SetGoState((GameObjectState)objectState);
                        break;
                    case 1:
                        obj.SetGoType((GameObjectTypes)objectState);
                        break;
                    case 2:
                        obj.SetGoArtKit((byte)objectState);
                        break;
                    case 3:
                        obj.SetGoAnimProgress(objectState);
                        break;
                    case 4:
                        obj.SendCustomAnim(objectState);
                        break;
                    case 5:
                        if (objectState < 0 || objectState > (uint)GameObjectDestructibleState.Rebuilding)
                            return false;

                        obj.SetDestructibleState((GameObjectDestructibleState)objectState);
                        break;
                    default:
                        break;
                }

                handler.SendSysMessage("Set gobject type {0} state {1}", objectType, objectState);
                return true;
            }
        }
    }
}
