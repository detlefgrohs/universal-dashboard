using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using NLog.Fluent;
using UniversalDashboard.Execution;
using UniversalDashboard.Interfaces;
using UniversalDashboard.Models;
using UniversalDashboard.Models.Basics;
using UniversalDashboard.Services;

namespace UniversalDashboard
{
    public static class DashboardHubContextExtensions
    {
        public static async Task ShowModal(this IHubContext<DashboardHub> hub, string clientId, Modal modal)
        {
            await hub.Clients.Client(clientId).SendAsync("showModal", modal);
        }
        public static async Task CloseModal(this IHubContext<DashboardHub> hub, string clientId)
        {
            await hub.Clients.Client(clientId).SendAsync("closeModal");
        }
        public static async Task ShowToast(this IHubContext<DashboardHub> hub, string clientId, object toast)
        {
            await hub.Clients.Client(clientId).SendAsync("showToast", toast);
        }
        public static async Task HideToast(this IHubContext<DashboardHub> hub, string clientId, string id)
        {
            await hub.Clients.Client(clientId).SendAsync("hideToast", id);
        }
        public static async Task RequestState(this IHubContext<DashboardHub> hub, string clientId, string componentId, string requestId)
        {
            await hub.Clients.Client(clientId).SendAsync("requestState", componentId, requestId);
        }

        public static async Task Redirect(this IHubContext<DashboardHub> hub, string clientId, string url, bool newWindow)
        {
            await hub.Clients.Client(clientId).SendAsync("redirect", url, newWindow);
        }

        public static async Task SetState(this IHubContext<DashboardHub> hub, string componentId, Element state)
        {
            await hub.Clients.All.SendAsync("setState", componentId, state);
        }

        public static async  Task SetState(this IHubContext<DashboardHub> hub, string clientId, string componentId, Element state)
        {
            await hub.Clients.Client(clientId).SendAsync("setState", componentId, state);
        }

        public static async Task AddElement(this IHubContext<DashboardHub> hub, string parentComponentId, object[] element)
        {
            await hub.Clients.All.SendAsync("addElement", parentComponentId, element);
        }

        public static async  Task AddElement(this IHubContext<DashboardHub> hub, string clientId, string parentComponentId, object[] element)
        {
            await hub.Clients.Client(clientId).SendAsync("addElement", parentComponentId, element);
        }

        public static async Task RemoveElement(this IHubContext<DashboardHub> hub, string clientId, string componentId)
        {
            await hub.Clients.Client(clientId).SendAsync("removeElement", componentId);
        }

        public static async Task RemoveElement(this IHubContext<DashboardHub> hub, string componentId)
        {
            await hub.Clients.All.SendAsync("removeElement", componentId);
        }

        public static async Task ClearElement(this IHubContext<DashboardHub> hub, string clientId, string componentId)
        {
            await hub.Clients.Client(clientId).SendAsync("clearElement", componentId);
        }

        public static async Task ClearElement(this IHubContext<DashboardHub> hub, string componentId)
        {
            await hub.Clients.All.SendAsync("clearElement", componentId);
        }

        public static async Task SyncElement(this IHubContext<DashboardHub> hub, string clientId, string componentId)
        {
            await hub.Clients.Client(clientId).SendAsync("syncElement", componentId);
        }

        public static async Task SyncElement(this IHubContext<DashboardHub> hub, string componentId)
        {
            await hub.Clients.All.SendAsync("syncElement", componentId);
        }
    }

    public class DashboardHub : Hub {
        private IExecutionService _executionService;
        private readonly StateRequestService _stateRequestService;
        private readonly IMemoryCache _memoryCache;
        private readonly IDashboardService _dashboardService;
        private static readonly Logger _logger = LogManager.GetLogger(nameof(DashboardHub));

        public DashboardHub(IExecutionService executionService, IMemoryCache memoryCache, StateRequestService stateRequestService, IDashboardService dashboardService) {
            Log.Debug("DashboardHub constructor");

            _executionService = executionService;
            _stateRequestService = stateRequestService;
            _memoryCache = memoryCache;
            _dashboardService = dashboardService;
        }

        public override async Task OnConnectedAsync()
        {
            await Task.FromResult(0);
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            await Task.FromResult(0);
            if (exception == null) {
                Log.Debug("Disconnected");
            }
            else
            {
                Log.Error(exception.Message);
            }

            _memoryCache.Remove(Context.ConnectionId);

            var sessionId = _memoryCache.Get(Context.ConnectionId);
            if (sessionId != null)
            {
                _memoryCache.Remove(sessionId);
                _dashboardService.EndpointService.EndSession(sessionId as string);
            }
        }

        public async Task SetSessionId(string sessionId)
        {
            Log.Debug($"SetSessionId({sessionId})");

            await Task.FromResult(0);

            _memoryCache.Set(Context.ConnectionId, sessionId);
            _memoryCache.Set(sessionId, Context.ConnectionId);
            _dashboardService.EndpointService.StartSession(sessionId);
        }

        public Task Reload()
        {
            Log.Debug($"Reload()");

            return Clients.All.SendAsync("reload");
        }

        public async Task RequestStateResponse(string requestId, Element state)
        {
            await Task.FromResult(0);

            _stateRequestService.Set(requestId, state);
        }

        public async Task UnregisterEvent(string eventId)
        {
            Log.Debug($"UnregisterEvent() {eventId}");

            await Task.CompletedTask;
            if (_memoryCache.TryGetValue(Context.ConnectionId, out string sessionId))
            {
                _dashboardService.EndpointService.Unregister(eventId, sessionId);
            }
            else
            {
                _dashboardService.EndpointService.Unregister(eventId, null);
            }
        }

        public Task ClientEvent(string eventId, string eventName, string eventData, string location) {
            _logger.Debug($"ClientEvent {eventId} {eventName}");

            

            var variables = new Dictionary<string, object>();
            var userName = Context.User?.Identity?.Name;

            if (!string.IsNullOrEmpty(userName))
            {
                variables.Add("user", userName);
            }

            if (!string.IsNullOrEmpty(location)) {
                var locationObject = JsonConvert.DeserializeObject<Location>(location);
                variables.Add("Location", locationObject);
			}

            if (bool.TryParse(eventData, out bool data))
            {
                variables.Add("EventData", data);
            }
            else
            {
                variables.Add("EventData", eventData);
            }

            variables.Add("MemoryCache", _memoryCache);

            try
            {
                _memoryCache.TryGetValue(Context.ConnectionId, out string sessionId);

                var endpoint = _dashboardService.EndpointService.Get(eventId, sessionId);
                if (endpoint == null)
                {
                    _logger.Warn($"Endpoint {eventId} not found.");
                    throw new Exception($"Endpoint {eventId} not found.");
                }

                var executionContext = new ExecutionContext(endpoint, variables, new Dictionary<string, object>(), Context.User);
                executionContext.ConnectionId = Context.ConnectionId;
                executionContext.SessionId = sessionId;

                return Task.Run(() =>
                {
                    try
                    {
                        
                        _executionService.ExecuteEndpoint(executionContext, endpoint);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error("Failed to execute action. " + ex.Message);
                        throw;
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to execute endpoint. " + ex.Message);
                throw;
            }
        }
    }
}