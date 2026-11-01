using System.Collections.Generic;
using Game.City;
using Game.Common;
using Game.Economy;
using Game.Prefabs;
using Game.Routes;
using Game.Vehicles;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace AutoBusLines
{
    public partial class BusLineGenerator
    {
        // A bus model the player can pick for generated lines, keyed by prefab name so the choice
        // survives a reload (prefab entities are not stable across sessions).
        public struct BusModelOption
        {
            public string Name;
            public Entity Prefab;
        }

        private TransportVehicleSelectData _transportVehicleSelectData;
        private EntityQuery _transportVehiclePrefabQuery;
        private CityConfigurationSystem _cityConfigurationSystem;

        // Routes waiting for their chosen model. Routes.InitializeSystem clears every Created
        // route's VehicleModel buffer and picks a random vehicle, so the choice can only be
        // written once the route has lost Created.
        private readonly List<(Entity route, bool isExpress)> _pendingVehicleModels = new List<(Entity, bool)>();

        // Whether each entry of _plannedTours is an express tour, kept 1:1 with _plannedTourColors.
        private List<bool> _plannedTourIsExpress = new List<bool>();

        private void CreateVehicleModelSelection()
        {
            _transportVehicleSelectData = new TransportVehicleSelectData(this);
            _transportVehiclePrefabQuery = GetEntityQuery(TransportVehicleSelectData.GetEntityQueryDesc());
            _cityConfigurationSystem = World.GetOrCreateSystemManaged<CityConfigurationSystem>();
        }

        /// <summary>
        /// Lists the unlocked bus models a passenger bus line can run, the same list the game's
        /// "Select vehicles" section offers for a bus line.
        /// </summary>
        public List<BusModelOption> ListBusModels()
        {
            var options = new List<BusModelOption>();
            if (!FindBusLinePrefab(out Entity linePrefab, out _) ||
                !EntityManager.HasComponent<TransportLineData>(linePrefab))
                return options;

            var lineData = EntityManager.GetComponentData<TransportLineData>(linePrefab);
            var primary = new NativeList<Entity>(16, Allocator.Temp);
            var secondary = new NativeList<Entity>(4, Allocator.Temp);
            try
            {
                _transportVehicleSelectData.PreUpdate(this, _cityConfigurationSystem, _transportVehiclePrefabQuery, Allocator.TempJob, out JobHandle handle);
                handle.Complete();
                _transportVehicleSelectData.ListVehicles(lineData.m_TransportType, EnergyTypes.FuelAndElectricity, lineData.m_SizeClass,
                    PublicTransportPurpose.TransportLine, Resource.NoResource, primary, secondary, ignoreTheme: true);
                _transportVehicleSelectData.PostUpdate(default);

                for (int i = 0; i < primary.Length; i++)
                {
                    string name = _prefabSystem.GetPrefabName(primary[i]);
                    if (!string.IsNullOrEmpty(name))
                        options.Add(new BusModelOption { Name = name, Prefab = primary[i] });
                }
            }
            finally
            {
                primary.Dispose();
                secondary.Dispose();
            }
            return options;
        }

        private Entity ResolveBusModel(string prefabName, List<BusModelOption> options)
        {
            if (string.IsNullOrEmpty(prefabName))
                return Entity.Null;
            foreach (var option in options)
            {
                if (option.Name == prefabName)
                    return option.Prefab;
            }
            log.Warn($"Bus model '{prefabName}' is not available (locked, removed or from a disabled asset pack); the game's own choice is kept.");
            return Entity.Null;
        }

        private void QueueVehicleModel(Entity routeEntity, bool isExpress)
        {
            string chosen = isExpress ? Mod.setting?.ExpressBusModel : Mod.setting?.LocalBusModel;
            if (!string.IsNullOrEmpty(chosen))
                _pendingVehicleModels.Add((routeEntity, isExpress));
        }

        private void ApplyPendingVehicleModels()
        {
            if (_pendingVehicleModels.Count == 0)
                return;

            List<BusModelOption> options = null;
            for (int i = _pendingVehicleModels.Count - 1; i >= 0; i--)
            {
                var (route, isExpress) = _pendingVehicleModels[i];
                if (!EntityManager.Exists(route) || EntityManager.HasComponent<Deleted>(route) ||
                    !EntityManager.HasBuffer<VehicleModel>(route))
                {
                    _pendingVehicleModels.RemoveAt(i);
                    continue;
                }
                if (EntityManager.HasComponent<Created>(route))
                    continue;

                options ??= ListBusModels();
                Entity model = ResolveBusModel(isExpress ? Mod.setting?.ExpressBusModel : Mod.setting?.LocalBusModel, options);
                if (model != Entity.Null)
                {
                    var buffer = EntityManager.GetBuffer<VehicleModel>(route);
                    buffer.Clear();
                    buffer.Add(new VehicleModel { m_PrimaryPrefab = model, m_SecondaryPrefab = Entity.Null });
                }
                _pendingVehicleModels.RemoveAt(i);
            }
        }
    }
}
