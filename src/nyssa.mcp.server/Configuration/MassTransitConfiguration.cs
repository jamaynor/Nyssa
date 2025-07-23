using MassTransit;
using Nyssa.Mcp.Server.Models;

namespace Nyssa.Mcp.Server.Configuration
{
    /// <summary>
    /// Configuration options for MassTransit message bus
    /// </summary>
    public class MassTransitOptions
    {
        public const string SectionName = "MassTransit";

        /// <summary>
        /// Transport type: "InMemory" or "RabbitMQ"
        /// </summary>
        public string Transport { get; set; } = "InMemory";

        /// <summary>
        /// RabbitMQ connection settings (only used when Transport = "RabbitMQ")
        /// </summary>
        public RabbitMqSettings RabbitMQ { get; set; } = new();

        /// <summary>
        /// Message retry policy settings
        /// </summary>
        public RetrySettings Retry { get; set; } = new();

        /// <summary>
        /// Circuit breaker settings
        /// </summary>
        public CircuitBreakerSettings CircuitBreaker { get; set; } = new();
    }

    public class RabbitMqSettings
    {
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 5672;
        public string Username { get; set; } = "guest";
        public string Password { get; set; } = "guest";
        public string VirtualHost { get; set; } = "/";
        public int PrefetchCount { get; set; } = 16;
        public int ConcurrentMessageLimit { get; set; } = 32;
    }

    public class RetrySettings
    {
        public int RetryLimit { get; set; } = 3;
        public TimeSpan InitialInterval { get; set; } = TimeSpan.FromSeconds(1);
        public TimeSpan MaxInterval { get; set; } = TimeSpan.FromSeconds(30);
        public double IntervalMultiplier { get; set; } = 2.0;
    }

    public class CircuitBreakerSettings
    {
        public int TripThreshold { get; set; } = 5;
        public TimeSpan ActiveThreshold { get; set; } = TimeSpan.FromSeconds(60);
        public TimeSpan ResetTimeout { get; set; } = TimeSpan.FromMinutes(5);
    }

    /// <summary>
    /// Extension methods for configuring MassTransit
    /// </summary>
    public static class MassTransitConfigurationExtensions
    {
        /// <summary>
        /// Configures MassTransit message bus with RBAC message consumers
        /// </summary>
        public static IServiceCollection AddRbacMassTransit(
            this IServiceCollection services, 
            IConfiguration configuration)
        {
            var options = configuration.GetSection(MassTransitOptions.SectionName).Get<MassTransitOptions>() 
                         ?? new MassTransitOptions();

            services.Configure<MassTransitOptions>(configuration.GetSection(MassTransitOptions.SectionName));

            services.AddMassTransit(x =>
            {
                // Configure message consumers (will be added in Phase 1.4)
                ConfigureConsumers(x);

                // Configure transport based on settings
                ConfigureTransport(x, options);

                // Configure global settings
                ConfigureGlobalSettings(x, options);
            });

            return services;
        }

        private static void ConfigureConsumers(IBusRegistrationConfigurator configurator)
        {
            // Register RBAC message handlers
            configurator.AddConsumer<Services.RbacMessageHandlers.UserResolutionHandler>();
            configurator.AddConsumer<Services.RbacMessageHandlers.PermissionResolutionHandler>();
            configurator.AddConsumer<Services.RbacMessageHandlers.OrganizationResolutionHandler>();
            configurator.AddConsumer<Services.RbacMessageHandlers.TokenManagementHandler>();
            configurator.AddConsumer<Services.RbacMessageHandlers.AuditLoggingHandler>();
            
            // Configure consumer endpoints with specific settings
            configurator.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter("rbac", false));
        }

        private static void ConfigureTransport(IBusRegistrationConfigurator configurator, MassTransitOptions options)
        {
            switch (options.Transport.ToLowerInvariant())
            {
                case "rabbitmq":
                    ConfigureRabbitMQ(configurator, options.RabbitMQ);
                    break;
                case "inmemory":
                default:
                    ConfigureInMemory(configurator);
                    break;
            }
        }

        private static void ConfigureRabbitMQ(IBusRegistrationConfigurator configurator, RabbitMqSettings settings)
        {
            configurator.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(settings.Host, h =>
                {
                    h.Username(settings.Username);
                    h.Password(settings.Password);
                });

                // Configure receive endpoints for all registered consumers
                cfg.ConfigureEndpoints(context);

                // Global RabbitMQ settings
                cfg.PrefetchCount = settings.PrefetchCount;
                cfg.ConcurrentMessageLimit = settings.ConcurrentMessageLimit;
            });
        }

        private static void ConfigureInMemory(IBusRegistrationConfigurator configurator)
        {
            configurator.UsingInMemory((context, cfg) =>
            {
                // Configure receive endpoints for all registered consumers
                cfg.ConfigureEndpoints(context);
            });
        }

        private static void ConfigureGlobalSettings(IBusRegistrationConfigurator configurator, MassTransitOptions options)
        {
            // Configure retry policy will be added in Phase 2 when we implement specific message handlers
            // For now, we'll use MassTransit defaults
        }

        /// <summary>
        /// Configures MassTransit request clients for sending messages
        /// </summary>
        public static IServiceCollection AddRbacMessageClients(this IServiceCollection services)
        {
            // Request clients will be configured here for request/response patterns
            // This will be expanded in Phase 2 when we add specific message types
            
            return services;
        }

        /// <summary>
        /// Creates a Result-wrapped message publish operation
        /// </summary>
        public static async Task<Result> PublishWithResultAsync<T>(
            this IPublishEndpoint publishEndpoint, 
            T message, 
            CancellationToken cancellationToken = default) where T : class
        {
            try
            {
                await publishEndpoint.Publish(message, cancellationToken);
                return Result.Ok();
            }
            catch (Exception ex)
            {
                return ErrorMessage.MessageBus(5104, $"Message bus publish operation failed for {typeof(T).Name}: {ex.Message}", "A system error occurred. Please try again later.");
            }
        }

        /// <summary>
        /// Creates a Result-wrapped message send operation
        /// </summary>
        public static async Task<Result> SendWithResultAsync<T>(
            this ISendEndpoint sendEndpoint, 
            T message, 
            CancellationToken cancellationToken = default) where T : class
        {
            try
            {
                await sendEndpoint.Send(message, cancellationToken);
                return Result.Ok();
            }
            catch (Exception ex)
            {
                return ErrorMessage.MessageBus(5104, $"Message bus send operation failed for {typeof(T).Name}: {ex.Message}", "A system error occurred. Please try again later.");
            }
        }
    }
}