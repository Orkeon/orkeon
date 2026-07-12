{
  "_comment": "Embedded local model (llama-server inside this container). Generated at image build time from local.json.tpl — __BASEURL__ and __MODEL__ are substituted by the local-llm Dockerfile stage. Disable the embedded server with -e ORKEON_LOCAL_LLM=0.",
  "Llm": {
    "Model": "__MODEL__",
    "BaseUrl": "__BASEURL__",
    "ApiKey": "not-needed",
    "Temperature": 0.7,
    "MaxTokens": 4096,
    "TimeoutSeconds": 300
  },
  "RateLimiting": {
    "MaxConcurrentRequests": 1,
    "GlobalRequestsPerMinute": 60,
    "ProviderRequestsPerMinute": 30,
    "AgentRequestsPerMinute": 20,
    "QueueLimit": 32
  }
}
