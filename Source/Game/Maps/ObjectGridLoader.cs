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
using Game.Entities;
using System;
using System.Collections.Generic;

namespace Game.Maps
{
    class ObjectGridLoader : Notifier
    {
        public ObjectGridLoader(Grid grid, Map map, Cell cell)
        {
            i_cell = new Cell(cell);

            i_grid = grid;
            i_map = map;
        }

        public void LoadN()
        {
            i_creatures = 0;
            i_gameObjects = 0;
            i_corpses = 0;
            i_cell.data.cell_y = 0;
            for (uint x = 0; x < MapConst.MaxCells; ++x)
            {
                i_cell.data.cell_x = x;
                for (uint y = 0; y < MapConst.MaxCells; ++y)
                {
                    i_cell.data.cell_y = y;

                    var visitor = new Visitor(this, GridMapTypeMask.AllGrid);
                    i_grid.VisitGrid(x, y, visitor);

                    ObjectWorldLoader worker = new(this);
                    visitor = new Visitor(worker, GridMapTypeMask.AllWorld);
                    i_grid.VisitGrid(x, y, visitor);
                }
            }
            Log.outDebug(LogFilter.Maps, "{0} GameObjects, {1} Creatures, and {2} Corpses/Bones loaded for grid {3} on map {4}", i_gameObjects, i_creatures, i_corpses, i_grid.GetGridId(), i_map.GetId());
        }

        public override void Visit(IList<GameObject> objs)
        {
            CellCoord cellCoord = i_cell.GetCellCoord();
            CellObjectGuids cellguids = Global.ObjectMgr.GetCellObjectGuids(i_map.GetId(), i_map.GetDifficultyID(), cellCoord.GetId());
            if (cellguids == null)
                return;

            LoadHelper<GameObject>(cellguids.gameobjects, cellCoord, ref i_gameObjects, i_map);
        }

        public override void Visit(IList<Creature> objs)
        {
            CellCoord cellCoord = i_cell.GetCellCoord();
            CellObjectGuids cellguids = Global.ObjectMgr.GetCellObjectGuids(i_map.GetId(), i_map.GetDifficultyID(), cellCoord.GetId());
            if (cellguids == null)
                return;

            LoadHelper<Creature>(cellguids.creatures, cellCoord, ref i_creatures, i_map);
        }

        public override void Visit(IList<AreaTrigger> objs)
        {
            CellCoord cellCoord = i_cell.GetCellCoord();
            SortedSet<ulong> areaTriggers = Global.AreaTriggerDataStorage.GetAreaTriggersForMapAndCell(i_map.GetId(), cellCoord.GetId());
            if (areaTriggers == null)
                return;

            LoadHelper<AreaTrigger>(areaTriggers, cellCoord, ref i_areaTriggers, i_map);
        }

        void LoadHelper<T>(SortedSet<ulong> guid_set, CellCoord cell, ref uint count, Map map) where T : WorldObject, new()
        {
            foreach (var guid in guid_set)
            {
                // Don't spawn at all if there's a respawn timer
                if (!map.ShouldBeSpawnedOnGridLoad<T>(guid))
                    continue;

                T obj = new();
                if (!obj.LoadFromDB(guid, map, false, false))
                {
                    obj.Dispose();
                    continue;
                }

                AddObjectHelper(cell, ref count, map, obj);
            }
        }

        void AddObjectHelper<T>(CellCoord cellCoord, ref uint count, Map map, T obj) where T : WorldObject
        {
            var cell = new Cell(cellCoord);
            map.AddToGrid(obj, cell);
            obj.AddToWorld();

            if (obj.IsCreature())
                if (obj.IsActiveObject())
                    map.AddToActive(obj);

            ++count;
        }

        public Cell i_cell;
        public Grid i_grid;
        public Map i_map;
        uint i_gameObjects;
        uint i_creatures;
        public uint i_corpses;
        uint i_areaTriggers;
    }

    class ObjectWorldLoader : Notifier
    {
        public ObjectWorldLoader(ObjectGridLoader gloader)
        {
            i_cell = gloader.i_cell;
            i_map = gloader.i_map;
            i_grid = gloader.i_grid;
            i_corpses = gloader.i_corpses;
        }

        public override void Visit(IList<Corpse> objs)
        {
            CellCoord cellCoord = i_cell.GetCellCoord();
            var corpses = i_map.GetCorpsesInCell(cellCoord.GetId());
            if (corpses != null)
            {
                foreach (Corpse corpse in corpses)
                {
                    corpse.AddToWorld();
                    var cell = i_grid.GetGridCell(i_cell.GetCellX(), i_cell.GetCellY());
                    if (corpse.IsWorldObject())
                    {
                        i_map.AddToGrid(corpse, new Cell(cellCoord));
                        cell.AddWorldObject(corpse);
                    }
                    else
                        cell.AddGridObject(corpse);

                    ++i_corpses;
                }
            }
        }

        Cell i_cell;
        Map i_map;
        Grid i_grid;

        public uint i_corpses;
    }

    //Stop the creatures before unloading the NGrid
    class ObjectGridStoper : Notifier
    {
        public override void Visit(IList<Creature> objs)
        {
            // stop any fights at grid de-activation and remove dynobjects/areatriggers created at cast by creatures
            for (var i = 0; i < objs.Count; ++i)
            {  
                Creature creature = objs[i];
                creature.RemoveAllDynObjects();
                creature.RemoveAllAreaTriggers();

                if (creature.IsInCombat())
                    creature.CombatStop();
            }
        }
    }

    //Move the foreign creatures back to respawn positions before unloading the NGrid
    class ObjectGridEvacuator : Notifier
    {
        public override void Visit(IList<Creature> objs)
        {
            for (var i = 0; i < objs.Count; ++i)
            {
                Creature creature = objs[i];
                // creature in unloading grid can have respawn point in another grid
                // if it will be unloaded then it will not respawn in original grid until unload/load original grid
                // move to respawn point to prevent this case. For player view in respawn grid this will be normal respawn.
                creature.GetMap().CreatureRespawnRelocation(creature, true);
            }
        }

        public override void Visit(IList<GameObject> objs)
        {
            for (var i = 0; i < objs.Count; ++i)
            {
                GameObject gameObject = objs[i];
                // gameobject in unloading grid can have respawn point in another grid
                // if it will be unloaded then it will not respawn in original grid until unload/load original grid
                // move to respawn point to prevent this case. For player view in respawn grid this will be normal respawn.
                gameObject.GetMap().GameObjectRespawnRelocation(gameObject, true);
            }
        }
    }

    //Clean up and remove from world
    class ObjectGridCleaner : Notifier
    {
        public override void Visit(IList<WorldObject> objs)
        {
            for (var i = 0; i < objs.Count; ++i)
            {
                WorldObject obj = objs[i];

                if (obj.IsTypeId(TypeId.Player))
                    continue;

                obj.CleanupsBeforeDelete();
            }       
        }
    }

    //Delete objects before deleting NGrid
    class ObjectGridUnloader : Notifier
    {
        public override void Visit(IList<WorldObject> objs)
        {
            for (var i = 0; i < objs.Count; ++i)
            {
                WorldObject obj = objs[i];

                if (obj.IsTypeId(TypeId.Corpse))
                    continue;

                //Some creatures may summon other temp summons in CleanupsBeforeDelete()
                //So we need this even after cleaner (maybe we can remove cleaner)
                //Example: Flame Leviathan Turret 33139 is summoned when a creature is deleted
                //TODO: Check if that script has the correct logic. Do we really need to summons something before deleting?
                obj.CleanupsBeforeDelete();
                obj.Dispose();
            }
        }
    }
}
