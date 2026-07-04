using Orkeon.Domain.Common;
using Orkeon.Domain.Agent.Composition;
using Orkeon.Domain.SharedKernel.ValueObjects;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;

namespace Orkeon.Domain.Tests.Composition;

/// <summary>
/// Tests for Crew Template records following Clean Architecture principles.
/// </summary>
public class CrewTemplateTests
{
    private static readonly string[] DesignPatterns = ["Circuit Breaker", "Service Discovery", "API Gateway"];
    private static readonly string[] DataSources = ["database", "api", "files"];
    private static readonly string[] MlAlgorithms = ["XGBoost", "Neural Network", "Random Forest"];
    private static readonly string[] MlMetrics = ["accuracy", "precision", "recall", "f1"];
    private static readonly string[] AuthLoggingCaching = ["auth", "logging", "caching"];

    #region CrewTemplate Tests

    [Fact]
    public void ShouldInitializeWithDefaults_WhenUsingCrewTemplateWithDefaultConstructor()
    {
        var template = new CrewTemplate();

        Assert.NotNull(template.Id);
        Assert.Equal(string.Empty, template.Name);
        Assert.Equal(string.Empty, template.Description);
        Assert.Equal(string.Empty, template.Goal);
        Assert.NotNull(template.RequiredAgents);
        Assert.Empty(template.RequiredAgents);
        Assert.NotNull(template.TaskTemplates);
        Assert.Empty(template.TaskTemplates);
        Assert.Equal(ProcessType.Sequential, template.ProcessType);
        Assert.NotNull(template.DefaultSettings);
        Assert.Empty(template.DefaultSettings);
        Assert.NotNull(template.Tags);
        Assert.Empty(template.Tags);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingCrewTemplateUsingProperties()
    {
        var id = CrewTemplateId.Create();
        var requiredAgents = new List<AgentTemplate>
        {
            new AgentTemplate { Role = RoleDeveloper },
            new AgentTemplate { Role = "Tester" }
        };
        var taskTemplates = new List<TaskTemplate>
        {
            new TaskTemplate { Description = GoalWriteCode },
            new TaskTemplate { Description = "Test code" }
        };
        var defaultSettings = new Dictionary<string, object>
        {
            { "max_iterations", 5 },
            { "timeout", 3600 },
            { "verbose", true }
        };
        var tags = new List<string> { "development", "agile", "software" };

        var template = new CrewTemplate
        {
            Id = id,
            Name = "Software Development Team",
            Description = "Template for standard software development crew",
            Goal = "Deliver high-quality software on time",
            RequiredAgents = requiredAgents,
            TaskTemplates = taskTemplates,
            ProcessType = ProcessType.Hierarchical,
            DefaultSettings = defaultSettings,
            Tags = tags
        };

        Assert.Equal(id, template.Id);
        Assert.Equal("Software Development Team", template.Name);
        Assert.Equal("Template for standard software development crew", template.Description);
        Assert.Equal("Deliver high-quality software on time", template.Goal);
        Assert.Equal(requiredAgents, template.RequiredAgents);
        Assert.Equal(taskTemplates, template.TaskTemplates);
        Assert.Equal(ProcessType.Hierarchical, template.ProcessType);
        Assert.Equal(defaultSettings, template.DefaultSettings);
        Assert.Equal(tags, template.Tags);
    }

    [Fact]
    public void ShouldHaveUniqueIds_WhenUsingCrewTemplateWithMultipleInstances()
    {
        var template1 = new CrewTemplate();
        var template2 = new CrewTemplate();
        var template3 = new CrewTemplate();

        Assert.NotEqual(template1.Id, template2.Id);
        Assert.NotEqual(template2.Id, template3.Id);
        Assert.NotEqual(template1.Id, template3.Id);
    }

    [Fact]
    public void ShouldSupportMutableDictionary_WhenUsingCrewTemplateWithDefaultSettings()
    {
        var template = new CrewTemplate
        {
            DefaultSettings = new Dictionary<string, object>
            {
                { "llm_model", ModelGpt4 },
                { "temperature", 0.7 },
                { "retry_count", 3 }
            }
        };

        // Dictionary is still mutable since it uses Dictionary<string, object> init
        template.DefaultSettings["temperature"] = 0.8;

        Assert.Equal(3, template.DefaultSettings.Count);
        Assert.Equal(ModelGpt4, template.DefaultSettings["llm_model"]);
        Assert.Equal(0.8, template.DefaultSettings["temperature"]);
    }

    [Fact]
    public void ShouldSupportDuplicates_WhenUsingCrewTemplateUsingTags()
    {
        var template = new CrewTemplate
        {
            Tags = ["important", "urgent", "important"]
        };

        Assert.Equal(3, template.Tags.Count);
        Assert.Equal(2, template.Tags.Count(t => t == "important"));
    }

    #endregion

    #region AgentTemplate Tests

    [Fact]
    public void ShouldInitializeWithDefaults_WhenUsingAgentTemplateWithDefaultConstructor()
    {
        var agent = new AgentTemplate();

        Assert.Equal(string.Empty, agent.Role);
        Assert.Equal(string.Empty, agent.Goal);
        Assert.NotNull(agent.RequiredSkills);
        Assert.Empty(agent.RequiredSkills);
        Assert.NotNull(agent.RequiredTools);
        Assert.Empty(agent.RequiredTools);
        Assert.True(agent.AllowDelegation);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingAgentTemplateUsingProperties()
    {
        var agent = new AgentTemplate
        {
            Role = "Senior Backend Developer",
            Goal = "Design and implement robust backend services",
            RequiredSkills = ["C#", "ASP.NET", "SQL", "Docker"],
            RequiredTools = ["Visual Studio", "Git", "Azure DevOps"],
            AllowDelegation = false
        };

        Assert.Equal("Senior Backend Developer", agent.Role);
        Assert.Equal("Design and implement robust backend services", agent.Goal);
        Assert.Equal(4, agent.RequiredSkills.Count);
        Assert.Equal(3, agent.RequiredTools.Count);
        Assert.False(agent.AllowDelegation);
    }

    [Fact]
    public void ShouldSupportCollections_WhenUsingAgentTemplateUsingCollections()
    {
        var agent = new AgentTemplate
        {
            RequiredSkills = ["Python", "TensorFlow"],
            RequiredTools = ["Jupyter", "PyCharm"]
        };

        Assert.Equal(2, agent.RequiredSkills.Count);
        Assert.Contains("Python", agent.RequiredSkills);
        Assert.Equal(2, agent.RequiredTools.Count);
    }

    #endregion

    #region TaskTemplate Tests

    [Fact]
    public void ShouldInitializeWithDefaults_WhenUsingTaskTemplateWithDefaultConstructor()
    {
        var task = new TaskTemplate();

        Assert.Equal(string.Empty, task.Description);
        Assert.Equal(string.Empty, task.ExpectedOutput);
        Assert.Null(task.RequiredRole);
        Assert.NotNull(task.Dependencies);
        Assert.Empty(task.Dependencies);
        Assert.NotNull(task.Parameters);
        Assert.Empty(task.Parameters);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingTaskTemplateUsingProperties()
    {
        var dependencies = new List<string> { "Task1", "Task2", "Task3" };
        var parameters = new Dictionary<string, object>
        {
            { "timeout", 300 },
            { "retries", 3 },
            { "output_format", "json" }
        };

        var task = new TaskTemplate
        {
            Description = "Implement user authentication API",
            ExpectedOutput = "REST API endpoints for login, logout, and token refresh",
            RequiredRole = "Backend Developer",
            Dependencies = dependencies,
            Parameters = parameters
        };

        Assert.Equal("Implement user authentication API", task.Description);
        Assert.Equal("REST API endpoints for login, logout, and token refresh", task.ExpectedOutput);
        Assert.Equal("Backend Developer", task.RequiredRole);
        Assert.Equal(dependencies, task.Dependencies);
        Assert.Equal(parameters, task.Parameters);
    }

    [Fact]
    public void ShouldCanBeNull_WhenUsingTaskTemplateUsingRequiredRole()
    {
        var task = new TaskTemplate
        {
            Description = "General task",
            RequiredRole = null
        };

        Assert.Null(task.RequiredRole);
    }

    [Fact]
    public void ShouldSupportComplexTypes_WhenUsingTaskTemplateUsingParameters()
    {
        var task = new TaskTemplate
        {
            Parameters = new Dictionary<string, object>
            {
                { "config", new { Host = "localhost", Port = 8080 } },
                { "features", AuthLoggingCaching },
                { "metadata", new Dictionary<string, string> { { "version", "1.0" }, { "author", "team" } } }
            }
        };

        Assert.Equal(3, task.Parameters.Count);
        Assert.NotNull(task.Parameters["config"]);
        Assert.IsType<string[]>(task.Parameters["features"]);
        Assert.IsType<Dictionary<string, string>>(task.Parameters["metadata"]);
    }

    #endregion

    #region Integration and Scenario Tests

    [Fact]
    public void ShouldScenario_WhenUsingCrewTemplateWithCompleteProjectTemplate()
    {
        var template = new CrewTemplate
        {
            Name = "Microservices Development Team",
            Description = "Template for developing microservices architecture",
            Goal = "Build scalable, distributed microservices system",
            ProcessType = ProcessType.Hierarchical,
            Tags = ["microservices", "distributed", "cloud-native"],
            RequiredAgents =
            [
                new AgentTemplate
                {
                    Role = "Solution Architect",
                    Goal = "Design overall system architecture and ensure technical coherence",
                    RequiredSkills = ["System Design", "Microservices", "Cloud Architecture", "Security"],
                    RequiredTools = ["Draw.io", "Kubernetes", "Docker", "Terraform"],
                    AllowDelegation = true
                },
                new AgentTemplate
                {
                    Role = "Backend Developer",
                    Goal = "Implement microservices with proper APIs and data management",
                    RequiredSkills = ["Java", "Spring Boot", "REST", "gRPC", "SQL", "NoSQL"],
                    RequiredTools = ["IntelliJ", "Postman", "Git", "Maven"],
                    AllowDelegation = true
                },
                new AgentTemplate
                {
                    Role = "DevOps Engineer",
                    Goal = "Setup CI/CD pipelines and manage infrastructure",
                    RequiredSkills = ["Docker", "Kubernetes", "CI/CD", "Monitoring", "Scripting"],
                    RequiredTools = ["Jenkins", "Helm", "Prometheus", "Grafana"],
                    AllowDelegation = false
                }
            ],
            TaskTemplates =
            [
                new TaskTemplate
                {
                    Description = "Design microservices architecture",
                    ExpectedOutput = "Architecture diagrams, service boundaries, API contracts",
                    RequiredRole = "Solution Architect",
                    Parameters = new Dictionary<string, object> { { "services_count", 5 }, { "include_patterns", DesignPatterns } }
                },
                new TaskTemplate
                {
                    Description = "Implement user service",
                    ExpectedOutput = "User microservice with CRUD operations and authentication",
                    RequiredRole = "Backend Developer",
                    Dependencies = ["Design microservices architecture"],
                    Parameters = new Dictionary<string, object> { { "database", "PostgreSQL" }, { "auth_type", "JWT" } }
                },
                new TaskTemplate
                {
                    Description = "Setup Kubernetes deployment",
                    ExpectedOutput = "K8s manifests, Helm charts, deployed services",
                    RequiredRole = "DevOps Engineer",
                    Dependencies = ["Implement user service"],
                    Parameters = new Dictionary<string, object> { { "replicas", 3 }, { "namespace", "production" } }
                }
            ],
            DefaultSettings = new Dictionary<string, object>
            {
                { "environment", "production" },
                { "monitoring_enabled", true },
                { "max_retry_attempts", 3 }
            }
        };

        Assert.Equal("Microservices Development Team", template.Name);
        Assert.Equal(3, template.RequiredAgents.Count);
        Assert.Equal(3, template.TaskTemplates.Count);
        Assert.Equal(ProcessType.Hierarchical, template.ProcessType);
        Assert.Equal(3, template.Tags.Count);
        Assert.Equal(3, template.DefaultSettings.Count);

        var roles = template.RequiredAgents.Select(a => a.Role).ToList();
        Assert.Contains("Solution Architect", roles);
        Assert.Contains("Backend Developer", roles);
        Assert.Contains("DevOps Engineer", roles);

        var dependentTasks = template.TaskTemplates.Count(t => t.Dependencies.Count > 0);
        Assert.Equal(2, dependentTasks);
        Assert.False(template.RequiredAgents.All(a => a.AllowDelegation));
    }

    [Fact]
    public void ShouldScenario_WhenUsingCrewTemplateUsingDataScienceTemplate()
    {
        var template = new CrewTemplate
        {
            Name = "Data Science Research Team",
            Description = "Template for ML/AI research projects",
            Goal = "Develop and deploy machine learning models",
            ProcessType = ProcessType.Sequential,
            RequiredAgents =
            [
                new AgentTemplate
                {
                    Role = "Data Engineer",
                    Goal = "Prepare and process data for analysis",
                    RequiredSkills = ["Python", "SQL", "ETL", "Data Pipelines"],
                    RequiredTools = ["Apache Spark", "Airflow", "dbt"]
                },
                new AgentTemplate
                {
                    Role = "Data Scientist",
                    Goal = "Build and evaluate ML models",
                    RequiredSkills = ["Machine Learning", "Statistics", "Python", "Deep Learning"],
                    RequiredTools = ["Jupyter", "TensorFlow", "scikit-learn", "MLflow"]
                },
                new AgentTemplate
                {
                    Role = "ML Engineer",
                    Goal = "Deploy and optimize models for production",
                    RequiredSkills = ["Python", "MLOps", "Model Optimization", "API Development"],
                    RequiredTools = ["Docker", "Kubernetes", "TensorFlow Serving"]
                }
            ],
            TaskTemplates =
            [
                new TaskTemplate
                {
                    Description = "Collect and prepare dataset",
                    ExpectedOutput = "Clean, preprocessed dataset ready for training",
                    RequiredRole = "Data Engineer",
                    Parameters = new Dictionary<string, object>
                    {
                        { "data_sources", DataSources },
                        { "target_size", "1M records" }
                    }
                },
                new TaskTemplate
                {
                    Description = "Train and evaluate models",
                    ExpectedOutput = "Trained model with performance metrics",
                    RequiredRole = "Data Scientist",
                    Dependencies = ["Collect and prepare dataset"],
                    Parameters = new Dictionary<string, object>
                    {
                        { "algorithms", MlAlgorithms },
                        { "metrics", MlMetrics }
                    }
                }
            ]
        };

        Assert.Equal(ProcessType.Sequential, template.ProcessType);
        Assert.Equal(3, template.RequiredAgents.Count);
        Assert.All(template.RequiredAgents, a => Assert.Contains("Python", a.RequiredSkills));
        Assert.Contains(template.TaskTemplates, t => t.Parameters.ContainsKey("algorithms"));
    }

    #endregion

    #region Edge Cases and Validation Tests

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingCrewTemplateWithNullCollections()
    {
        var crewTemplate = new CrewTemplate { RequiredAgents = null!, TaskTemplates = null!, DefaultSettings = null!, Tags = null! };
        var agentTemplate = new AgentTemplate { RequiredSkills = null!, RequiredTools = null! };
        var taskTemplate = new TaskTemplate { Dependencies = null!, Parameters = null! };

        Assert.Null(crewTemplate.RequiredAgents);
        Assert.Null(crewTemplate.TaskTemplates);
        Assert.Null(crewTemplate.DefaultSettings);
        Assert.Null(crewTemplate.Tags);
        Assert.Null(agentTemplate.RequiredSkills);
        Assert.Null(agentTemplate.RequiredTools);
        Assert.Null(taskTemplate.Dependencies);
        Assert.Null(taskTemplate.Parameters);
    }

    [Fact]
    public void ShouldBeAccepted_WhenUsingCrewTemplateWithEmptyId()
    {
        var template = new CrewTemplate();
        Assert.NotNull(template.Id);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingCrewTemplateWithUnicodeContent()
    {
        var template = new CrewTemplate
        {
            Name = "国际化团队 🌍",
            Description = "多语言支持的开发团队",
            Goal = "构建全球化应用 🚀",
            Tags = ["多语言", "国际化", "🌏"],
            RequiredAgents =
            [
                new AgentTemplate
                {
                    Role = "国际化工程师",
                    Goal = "实现多语言支持",
                    RequiredSkills = ["i18n", "本地化", "翻译API"]
                }
            ],
            TaskTemplates =
            [
                new TaskTemplate
                {
                    Description = "添加中文支持 🇨🇳",
                    ExpectedOutput = "完整的中文界面",
                    Parameters = new Dictionary<string, object>
                    {
                        { "语言代码", "zh-CN" },
                        { "字符集", "UTF-8" }
                    }
                }
            ]
        };

        Assert.Contains("🌍", template.Name);
        Assert.Contains("🚀", template.Goal);
        Assert.Contains("🌏", template.Tags);
        Assert.Contains("国际化工程师", template.RequiredAgents[0].Role);
        Assert.Contains("🇨🇳", template.TaskTemplates[0].Description);
        Assert.Contains("语言代码", template.TaskTemplates[0].Parameters.Keys);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingProcessTypeWithAllValues()
    {
        var allProcessTypes = new[] { ProcessType.Sequential, ProcessType.Hierarchical, ProcessType.Consensual, ProcessType.Parallel };
        var templates = new List<CrewTemplate>();
        foreach (var processType in allProcessTypes)
        {
            templates.Add(new CrewTemplate { ProcessType = processType });
        }

        Assert.Equal(allProcessTypes.Length, templates.Count);
        Assert.Contains(templates, t => t.ProcessType == ProcessType.Sequential);
        Assert.Contains(templates, t => t.ProcessType == ProcessType.Hierarchical);
    }

    [Fact]
    public void ShouldBeAllowed_WhenUsingCrewTemplateUsingCircularDependencies()
    {
        var template = new CrewTemplate
        {
            TaskTemplates =
            [
                new TaskTemplate { Description = "Task A", Dependencies = ["Task B"] },
                new TaskTemplate { Description = "Task B", Dependencies = ["Task C"] },
                new TaskTemplate { Description = "Task C", Dependencies = ["Task A"] }
            ]
        };

        Assert.Equal(3, template.TaskTemplates.Count);
        Assert.All(template.TaskTemplates, t => Assert.Single(t.Dependencies));
    }

    [Fact]
    public void ShouldProvideReadableRepresentation_WhenUsingTemplatesToString()
    {
        var crewTemplate = new CrewTemplate { Name = "Test Crew" };
        var agentTemplate = new AgentTemplate { Role = "Test Agent" };
        var taskTemplate = new TaskTemplate { Description = "Test Task" };

        Assert.NotNull(crewTemplate.ToString());
        Assert.NotNull(agentTemplate.ToString());
        Assert.NotNull(taskTemplate.ToString());
    }

    #endregion
}
