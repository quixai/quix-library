using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Quix.SqlServer.Writer.Configuration;
using Quix.SqlServer.Writer.Helpers.QuixApi.Portal;
using Quix.SqlServer.Writer.Helpers.QuixApi.Portal.Requests;
using Quix.Sdk.Process.Configuration;
using Quix.Sdk.Process.Kafka;
using Quix.Sdk.Streaming.Exceptions;
using Quix.Sdk.Streaming.QuixApi;
using Quix.Sdk.Transport.Fw;
using Exception = System.Exception;
using Workspace = Quix.SqlServer.Writer.Helpers.QuixApi.Portal.Workspace;

namespace Quix.SqlServer.Writer.Helpers
{
    public class QuixConfigHelper
    {
        private readonly BrokerConfiguration brokerConfiguration;
        private readonly ILogger logger;
        private readonly string token;
        private readonly Uri apiUrl;
        private readonly HttpClient httpClient;
        private const string WorkspaceIdEnvironmentKey = "Quix__Workspace__Id";
        private const string PortalApiEnvironmentKey = "Quix__Portal__Api";
        private const string SdkTokenKey = "Quix__Sdk__Token";

        public QuixConfigHelper(ILoggerFactory loggerFactory, HttpClient httpClient, BrokerConfiguration brokerConfiguration)
        {
            this.brokerConfiguration = brokerConfiguration;
            this.httpClient = httpClient;
            this.logger = loggerFactory.CreateLogger<QuixConfigHelper>();
            this.token = Environment.GetEnvironmentVariable(SdkTokenKey);
            if (string.IsNullOrWhiteSpace(this.token))
            {
                throw new InvalidConfigurationException($"Token must be given as an argument or set in {SdkTokenKey} environment variable.");
            }
            logger.LogTrace("Using token from environment variable {1}", SdkTokenKey);
            var envUri = Environment.GetEnvironmentVariable(PortalApiEnvironmentKey)?.ToLowerInvariant().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(envUri))
            {
                envUri = "https://portal-api.platform.quix.ai";
                logger.LogInformation($"Defaulting Portal Api to {envUri}");
            }
            logger.LogTrace("Using {0} endpoint for portal, configured from env var {1}", envUri, PortalApiEnvironmentKey);
            this.apiUrl = new Uri(envUri);
        }
        
        public async Task<(KafkaReaderConfiguration config, string topicId)> GetConfiguration()
        {
            var sw = Stopwatch.StartNew();
            var ws = await this.GetWorkspaceFromConfiguration(this.brokerConfiguration.TopicName);
            var client = await this.GetKafkaReaderConfigurationForWorkspace(ws);
            sw.Stop();
            this.logger.LogTrace("Created streaming client for workspace {0} in {1}.", ws.WorkspaceId, sw.Elapsed);

            sw = Stopwatch.StartNew();
            var topicId = await this.ValidateTopicExistence(ws, this.brokerConfiguration.TopicName);
            sw.Stop();
            this.logger.LogTrace("Validated topic {0} in {1}.", this.brokerConfiguration.TopicName, sw.Elapsed);
            return (client, topicId);
        }

        /// <returns>TopicID</returns>
        private async Task<string> ValidateTopicExistence(Workspace workspace, string topicName)
        {
            this.logger.LogTrace("Checking if topic {0} is already created.", topicName);
            var topics = await this.GetTopics(workspace);
            var existingTopic = topics.FirstOrDefault(y=> y.Name.Equals(topicName));
            
            if (existingTopic == null)
            {
                this.logger.LogInformation("Topic {0} is not yet created, creating in workspace {1} due to active settings.", topicName, workspace.WorkspaceId);
                existingTopic = await this.CreateTopic(workspace, topicName);
                this.logger.LogTrace("Created topic {0}.", topicName);
            }
            else
            {
                this.logger.LogTrace("Topic {0} is already created.", topicName);
            }
            
            while (existingTopic.Status == TopicStatus.Creating)
            {
                this.logger.LogInformation("Topic {0} is still creating.", topicName);
                await Task.Delay(1000);
                existingTopic = await this.GetTopic(workspace, topicName);
            }

            switch (existingTopic.Status)
            {
                case TopicStatus.Deleting:
                    throw new InvalidConfigurationException("Topic {0} is being deleted, not able to use.");
                case TopicStatus.Ready:
                    this.logger.LogDebug("Topic {0} is available for streaming.", topicName);
                    break;
                default:
                    this.logger.LogWarning("Topic {0} is in state {1}, but expected {2}.", topicName, existingTopic.Status, TopicStatus.Ready);
                    break;
            }

            return existingTopic.Id;
        }

        private async Task<Workspace> GetWorkspaceFromConfiguration(string topicName)
        {
            var workspaces = await this.GetWorkspaces();
            
            this.logger.LogTrace("Checking if workspace matching topic {0} exists", topicName);
            Workspace matchingWorkspace = null;
            
            // Assume it is a name, in which case the workspace comes from environment variables or token
            // Environment variable check
            var envWs = Environment.GetEnvironmentVariable(WorkspaceIdEnvironmentKey);
            if (!string.IsNullOrWhiteSpace(envWs))
            {
                matchingWorkspace = workspaces.FirstOrDefault(y => y.WorkspaceId == envWs);
                if (matchingWorkspace != null)
                {
                    this.logger.LogTrace("Found workspace using environment variable where topic {0} can be present, called {1}.", topicName, matchingWorkspace.Name);
                    return matchingWorkspace;
                }

                throw new InvalidConfigurationException($"The workspace id specified ({envWs}) in environment variable {WorkspaceIdEnvironmentKey} is not available. Typo or token without access to it?");
            }

            // Token check. If it is not a single workspace, we wouldn't know what ws you would want to use.
            if (workspaces.Count == 1)
            {
                matchingWorkspace = workspaces.First();
                this.logger.LogTrace("Found workspace using token where topic {0} can be present, called {1}.", topicName, matchingWorkspace.Name);
                return matchingWorkspace;
            }
            
            throw new InvalidConfigurationException($"No workspace could be identified for topic {topicName}. Verify the topic name is correct. If name is provided then {WorkspaceIdEnvironmentKey} environment variable or token with access to 1 workspace only must be provided. Current token has access to {workspaces.Count} workspaces and env var is unset.");
        }

        private async Task<KafkaReaderConfiguration> GetKafkaReaderConfigurationForWorkspace(Workspace ws)
        {
            if (ws.Broker == null)
            {
                throw new InvalidConfigurationException("Unable to configure broker due missing credentials.");
            }
            
            if (ws.Status != WorkspaceStatus.Ready)
            {
                if (ws.Status == WorkspaceStatus.Deleting)
                {
                    throw new InvalidConfigurationException($"Workspace {ws.WorkspaceId} is being deleted.");
                }
                this.logger.LogWarning("Workspace {0} is in state {1} instead of {2}.", ws.WorkspaceId, ws.Status, WorkspaceStatus.Ready);
            }

            var certPath = await this.GetWorkspaceCertificatePath(ws);
            if (!Enum.TryParse(ws.Broker.SaslMechanism.ToString(), true, out Confluent.Kafka.SaslMechanism parsed))
            {
                throw new ArgumentOutOfRangeException(nameof(ws.Broker.SaslMechanism), "Unsupported sasl mechanism " + ws.Broker.SaslMechanism);
            }
            

            var brokerSecuProperties = new SecurityOptionsBuilder().SetSslEncryption(certPath).SetSaslAuthentication(ws.Broker.Username, ws.Broker.Password, parsed).Build();      
            var brokerProperties = (this.brokerConfiguration.Properties ?? new Dictionary<string, string>()).ToDictionary(x=> x.Key, x=> x.Value);
            this.logger?.LogDebug("Broker Address = {address}, Broker Properties = {@brokerProperties}", ws.Broker.Address, brokerProperties);
            foreach (var secuSetting in brokerSecuProperties)
            {
                brokerProperties[secuSetting.Key] = secuSetting.Value;
            }
            
            
            if (brokerConfiguration.CommitAfterEveryCount == null)
            {
                throw new Exception("CommitAfterEveryCount can not be set to null to avoid deadlocks");
            }

            return new KafkaReaderConfiguration(ws.Broker.Address, $"{ws.WorkspaceId}-writer.SqlServer", brokerProperties)
            {
                CommitOptions = new CommitOptions()
                {
                    CommitEvery = this.brokerConfiguration.CommitAfterEveryCount,
                    CommitInterval = this.brokerConfiguration.CommitAfterMs,
                    AutoCommitEnabled = true
                }
            };
        }

        private async Task<string> GetWorkspaceCertificatePath(Workspace ws)
        {
            if (!ws.Broker.HasCertificate) return null;
            var targetFolder = Path.Combine(Directory.GetCurrentDirectory(), "certificates", ws.WorkspaceId);
            var certPath = Path.Combine(targetFolder, "ca.cert");
            if (!File.Exists(certPath))
            {
                if (!File.Exists(certPath))
                {
                    async Task<string> HelperFunc()
                    {
                        Directory.CreateDirectory(targetFolder);
                        this.logger.LogTrace("Certificate is not yet downloaded for workspace {0}.", ws.Name);
                        var zipPath = Path.Combine(targetFolder, "certs.zip");
                        if (!File.Exists(zipPath))
                        {
                            this.logger.LogTrace("Downloading certificate for workspace {0}.", ws.Name);
                            var response = await this.SendRequestToApi(HttpMethod.Get, new Uri(this.apiUrl, $"workspaces/{ws.WorkspaceId}/certificates"));
                            if (response.StatusCode == HttpStatusCode.NoContent)
                            {
                                ws.Broker.HasCertificate = false;
                                return null;
                            }

                            using var fs = File.Open(zipPath, FileMode.Create);
                            await response.Content.CopyToAsync(fs);
                        }

                        var hasCert = false;

                        using (var file = File.OpenRead(zipPath))
                        using (var zip = new ZipArchive(file, ZipArchiveMode.Read))
                        {
                            foreach (var entry in zip.Entries)
                            {
                                if (entry.Name != "ca.cert") continue;
                                using var stream = entry.Open();
                                using var fs = File.Open(certPath, FileMode.Create);
                                await stream.CopyToAsync(fs);
                                hasCert = true;
                            }
                        }
                        File.Delete(zipPath);
                        this.logger.LogTrace("Certificate is now available for workspace {0}", ws.Name);
                        if (!hasCert)
                        {
                            this.logger.LogWarning("Expected to find certificate for workspace {0}, but the downloaded zip had none.", ws.Name);
                            return null;
                        }
                        return certPath;
                    }
                    return HelperFunc().ConfigureAwait(true).GetAwaiter().GetResult();
                }
                this.logger.LogTrace("Certificate is downloaded by another thread for workspace {0}", ws.Name);
            }
            else
            {
                this.logger.LogTrace("Certificate is already available for workspace {0}", ws.Name);
            }

            return certPath;
        }

        private async Task<List<Workspace>> GetWorkspaces()
        {
            var result = await this.GetModelFromApi<List<Workspace>>("workspaces");
            if (result.Count == 0) throw new InvalidTokenException("Could not find any workspaces for this token.");
            return result;
        }
        
        private Task<List<Topic>> GetTopics(Workspace workspace)
        {
            return this.GetModelFromApi<List<Topic>>($"{workspace.WorkspaceId}/topics");
        }
        
        private Task<Topic> GetTopic(Workspace workspace, string topicName)
        {
            return this.GetModelFromApi<Topic>($"{workspace.WorkspaceId}/topics/{topicName}");
        }
        
        private async Task<Topic> CreateTopic(Workspace workspace, string topicName)
        {
            var uri = new Uri(this.apiUrl, $"{workspace.WorkspaceId}/topics");
            var response = await this.SendRequestToApi(HttpMethod.Post, uri, new TopicCreateRequest() {Name = topicName});
            
            var content = await response.Content.ReadAsStringAsync();
            try
            {
                var converted = JsonConvert.DeserializeObject<Topic>(content);
                return converted;
            }
            catch
            {
                throw new QuixApiException(uri.AbsolutePath, $"Failed to serialize response while creating topic {topicName}.", String.Empty, HttpStatusCode.OK);
            }
        }

        private async Task<HttpResponseMessage> SendRequestToApi(HttpMethod method, Uri uri, object bodyModel = null)
        {
            var httpRequest = new HttpRequestMessage(method, uri);
            if (bodyModel != null)
            {
                httpRequest.Content = new StringContent(JsonConvert.SerializeObject(bodyModel), Encoding.UTF8, "application/json");
            }
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", this.token);
            var response = await this.httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                this.logger.LogTrace("Request {0} had {1} response:{2}{3}", uri.AbsolutePath, response.StatusCode, Environment.NewLine, responseContent);
                string msg;
                var cid = String.Empty;
                try
                {
                    var error = JsonConvert.DeserializeObject<QuixApi.Portal.Exception>(responseContent);
                    msg = error.Message;
                    cid = error.CorrelationId;
                }
                catch
                {
                    msg = responseContent;
                }

                throw new QuixApiException(uri.AbsolutePath, msg, cid, response.StatusCode);
            }

            return response;
        }
        
        

        private async Task<T> GetModelFromApi<T>(string path) where T : class
        {
            var uri = new Uri(this.apiUrl, path);
            var response = await this.SendRequestToApi(HttpMethod.Get, uri);
            var responseContent = await response.Content.ReadAsStringAsync();
            this.logger.LogTrace("Request {0} had {1} response:{2}{3}", uri.AbsolutePath, response.StatusCode, Environment.NewLine, responseContent);
            try
            {
                var converted = JsonConvert.DeserializeObject<T>(responseContent);
                return converted;
            }
            catch
            {
                throw new QuixApiException(uri.AbsolutePath, "Failed to serialize response.", String.Empty, HttpStatusCode.OK);
            }
        }
    }
}