﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EventStore.ClientAPI.Projections;
using EventStore.ClientAPI.UserManagement;
using Microsoft.Extensions.Logging;

namespace SoftwarePioniere.EventStore
{
    // ReSharper disable once ClassNeverInstantiated.Global
    public class EventStoreSetup
    {
        private readonly ILogger _logger;
        private readonly EventStoreConnectionProvider _provider;

        public EventStoreSetup(EventStoreConnectionProvider provider, ILoggerFactory loggerFactory)
        {
             if (loggerFactory == null) throw new ArgumentNullException(nameof(loggerFactory));
            _logger = loggerFactory.CreateLogger(GetType());
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));

        }
        
        public async Task AddOpsUserToAdminsAsync()
        {
            _logger.LogDebug("AddOpsUserToAdminsAsync");

            if (await CheckOpsUserIsInAdminGroupAsync().ConfigureAwait(false))
            {
                _logger.LogDebug("ops user is already admin");
                return;
            }

            var manager = CreateUsersManager();

            var ops = await manager.GetUserAsync("ops", _provider.AdminCredentials).ConfigureAwait(false);
            if (ops.Groups == null || ops.Groups != null && !ops.Groups.Contains("$admins"))
            {
                var groups = new List<string>();
                if (ops.Groups != null)
                    groups.AddRange(ops.Groups);

                groups.Add("$admins");
                await manager.UpdateUserAsync("ops", ops.FullName, groups.ToArray(), _provider.AdminCredentials).ConfigureAwait(false);

                _logger.LogDebug("Group $admin added to ops user");
            }
        }

        public void AllowOpsUserOnStream(string name)
        {
            _logger.LogDebug("AllowOpsUserOnStream: {StreamName}", name);

            //var conn = _provider._provider.Connection.Value.Value;

            //  var set = new SystemSettings(new StreamAcl(),  );

            throw new NotImplementedException();
        }

        public async Task<bool> CheckContinousProjectionIsCreatedAsync(string name, string query)
        {
            _logger.LogDebug("CheckContinousProjectionAsync: {ProjectionName}", name);

            var manager = CreateProjectionsManager();

            var list = await manager.ListContinuousAsync(_provider.AdminCredentials).ConfigureAwait(false);
            var proj = list.FirstOrDefault(x => x.Name == name);

            if (proj != null)
            {
                _logger.LogDebug("Projection found, compare");
                var existingQuery = await manager.GetQueryAsync(name, _provider.AdminCredentials).ConfigureAwait(false);

                if (EqualsCleanStrings(existingQuery, query))
                    return true;
            }

            return false;
        }

        public async Task<bool> CheckOpsUserIsInAdminGroupAsync()
        {
            _logger.LogDebug("CheckOpsUserIsInAdminGroupAsync");

            var manager = new UsersManager(new EventStoreLogger(_logger), _provider.GetHttpIpEndpoint(),
                TimeSpan.FromSeconds(5));

            var ops = await manager.GetUserAsync("ops", _provider.AdminCredentials).ConfigureAwait(false);

            if (ops.Groups == null || ops.Groups != null && !ops.Groups.Contains("$admins"))
            {
                _logger.LogDebug("Ops User not in admins");
                return false;
            }

            _logger.LogDebug("Ops User in admins");
            return true;
        }

        public async Task<bool> CheckProjectionIsRunningAsync(string name)
        {
            _logger.LogDebug("CheckProjectionIsRunningAsync: {ProjectionName}", name);

            var manager = CreateProjectionsManager();

            var list = await manager.ListContinuousAsync(_provider.AdminCredentials).ConfigureAwait(false);

            var proj = list.FirstOrDefault(x => x.Name == name);
            if (proj == null)
                throw new InvalidOperationException($"Projection {name} not found. Please create it");

            if (proj.Status.Equals("Running", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Projection {ProjectionName} Running", name);
                return true;
            }
            _logger.LogDebug("Projection {ProjectionName} Not Running {Status}", name, proj.Status);
            return false;
        }

        private static string CleanString(string dirty)
        {
            var clean = dirty.Replace(" ", string.Empty).Replace("\n", string.Empty).Replace("\r", string.Empty);
            return clean;
        }

        /// <summary>
        /// </summary>
        /// <param name="name"></param>
        /// <param name="query"></param>
        public async Task CreateContinousProjectionAsync(string name, string query)
        {
            _logger.LogDebug("CreateContinousProjectionAsync: {ProjectionName}", name);

            var exists = await CheckContinousProjectionIsCreatedAsync(name, query).ConfigureAwait(false);

            if (exists)
            {
                _logger.LogDebug("ContinousProjection: {ProjectionName} already exist", name);
                return;
            }

            var manager = CreateProjectionsManager();

            var list = await manager.ListContinuousAsync(_provider.AdminCredentials).ConfigureAwait(false);

            var proj = list.FirstOrDefault(x => x.Name == name);
            if (proj != null)
            {
                _logger.LogDebug("Projection Query different Found: {ProjectionName}. Try Disable and Update", name);
                await manager.DisableAsync(name, _provider.AdminCredentials).ConfigureAwait(false);
                await manager.UpdateQueryAsync(name, query, _provider.AdminCredentials);
                await manager.EnableAsync(name, _provider.AdminCredentials);
                await Task.Delay(1000).ConfigureAwait(false);
            }
            else
            {
                _logger.LogDebug("Projection Not Found: {ProjectionName}. Try Create", name);
                await manager.CreateContinuousAsync(name, query, _provider.AdminCredentials).ConfigureAwait(false);
                await manager.EnableAsync(name, _provider.AdminCredentials);
                await Task.Delay(1000).ConfigureAwait(false);
            }


            exists = await CheckContinousProjectionIsCreatedAsync(name, query).ConfigureAwait(false);

            _logger.LogDebug("Projection: {ProjectionName}. Created. {exists}", name, exists);
        }

        private ProjectionsManager CreateProjectionsManager()
        {
            var manager = new ProjectionsManager(new EventStoreLogger(_logger), _provider.GetHttpIpEndpoint(),
                TimeSpan.FromSeconds(5));
            return manager;
        }

        private UsersManager CreateUsersManager()
        {
            var manager = new UsersManager(new EventStoreLogger(_logger), _provider.GetHttpIpEndpoint(),
                TimeSpan.FromSeconds(5));
            return manager;
        }


        public async Task DisableProjectionAsync(string name)
        {
            var isRunning = await CheckProjectionIsRunningAsync(name).ConfigureAwait(false);
            if (isRunning)
            {
                var manager = new ProjectionsManager(new EventStoreLogger(_logger), _provider.GetHttpIpEndpoint(),
                    TimeSpan.FromSeconds(5));

                _logger.LogDebug("Projection running: {ProjectionName}. Try Disabling", name);
                await manager.DisableAsync(name, _provider.AdminCredentials).ConfigureAwait(false);
            }
            else
            {
                _logger.LogDebug("Projection {ProjectionName} is not Running", name);                
            }

        }

        /// <summary>
        /// </summary>
        /// <param name="name"></param>
        /// <exception cref="InvalidOperationException"></exception>
        public async Task EnableProjectionAsync(string name)
        {
            _logger.LogDebug("EnableProjectionAsync: {ProjectionName}", name);

            var isRunning = await CheckProjectionIsRunningAsync(name).ConfigureAwait(false);
            if (isRunning)
            {
                _logger.LogDebug("Projection {ProjectionName} already Running", name);
                return;
            }

            var manager = new ProjectionsManager(new EventStoreLogger(_logger), _provider.GetHttpIpEndpoint(),
                TimeSpan.FromSeconds(5));

            _logger.LogDebug("Projection Not running: {ProjectionName}. Try Enabling", name);
            await manager.EnableAsync(name, _provider.AdminCredentials).ConfigureAwait(false);

            isRunning = await CheckProjectionIsRunningAsync(name).ConfigureAwait(false);
            var i = 1;
            while (i < 10 && !isRunning)
            {
                _logger.LogDebug("Waiting for Projection {ProjectionName} enabled. {i}", name);
                await Task.Delay(2000).ConfigureAwait(false);
                i++;
                isRunning = await CheckProjectionIsRunningAsync(name).ConfigureAwait(false);
            }

            isRunning = await CheckProjectionIsRunningAsync(name).ConfigureAwait(false);
            _logger.LogDebug("Projection {ProjectionName} running : {running}", name, isRunning);
        }

        private static bool EqualsCleanStrings(string value1, string value2)
        {
            var clean1 = CleanString(value1);
            var clean2 = CleanString(value2);

            var eq = string.Equals(clean1, clean2, StringComparison.OrdinalIgnoreCase);

            return eq;
        }


        //public async Task InsertEmptyEventIfStreamIsEmpty(string streamName)
        //{
        //    bool isEmpty;

        //    try
        //    {
        //        isEmpty = await _provider.IsStreamEmptyAsync(streamName).ConfigureAwait(false);

        //    }
        //    catch (Exception e)
        //    {
        //        isEmpty = true;
        //        Console.WriteLine(e);
        //    }

        //    if (isEmpty)
        //    {
        //        await InsertEmptyEventAsync(streamName);
        //    }

        //}


        //public async Task InsertEmptyEventAsync(string streamName)
        //{

        //    _logger.LogDebug("InsertEmptyEvent to Stream {StreamName}", streamName);

        //    var events = new[] { new EmptyDomainEvent().ToEventData(null) };

        //    var name = streamName; // $"{aggregateName}-empty";

        //    //wenn es eine category stream ist, dann den basis stream finden und eine neue gruppe -empty erzeugen
        //    if (streamName.Contains("$ce-"))
        //        name = $"{streamName.Replace("$ce-", string.Empty)}-empty";

        //    _logger.LogDebug("InsertEmptyEvent: StreamName {StreamName}", name);
        //    await _provider.Connection.Value.AppendToStreamAsync(name, -1, events, _provider.OpsCredentials).ConfigureAwait(false);

        //}
    }
}