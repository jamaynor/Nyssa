using MassTransit;
using Nyssa.Mcp.Server.Models;

namespace Nyssa.Mcp.Server.Configuration
{
    /// <summary>
    /// Simple test message and consumer to verify MassTransit configuration
    /// </summary>
    public record TestMessage
    {
        public string Content { get; init; } = string.Empty;
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Test consumer to verify MassTransit message handling works
    /// </summary>
    public class TestMessageConsumer : IConsumer<TestMessage>
    {
        private readonly ILogger<TestMessageConsumer> _logger;

        public TestMessageConsumer(ILogger<TestMessageConsumer> logger)
        {
            _logger = logger;
        }

        public Task Consume(ConsumeContext<TestMessage> context)
        {
            _logger.LogInformation("Received test message: {Content} at {Timestamp}", 
                context.Message.Content, 
                context.Message.Timestamp);
            
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Service for testing MassTransit functionality
    /// </summary>
    public interface IMassTransitTestService
    {
        Task<Result> SendTestMessageAsync(string content);
    }

    public class MassTransitTestService : IMassTransitTestService
    {
        private readonly IPublishEndpoint _publishEndpoint;
        private readonly ILogger<MassTransitTestService> _logger;

        public MassTransitTestService(
            IPublishEndpoint publishEndpoint,
            ILogger<MassTransitTestService> logger)
        {
            _publishEndpoint = publishEndpoint;
            _logger = logger;
        }

        public async Task<Result> SendTestMessageAsync(string content)
        {
            try
            {
                var message = new TestMessage { Content = content };
                
                _logger.LogInformation("Publishing test message: {Content}", content);
                
                return await _publishEndpoint.PublishWithResultAsync(message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send test message: {Error}", ex.Message);
                return ErrorMessage.MessageBus(5100, $"Failed to publish test message: {ex.Message}", "Test message failed");
            }
        }
    }

    /// <summary>
    /// Extension methods for registering test services
    /// </summary>
    public static class MassTransitTestExtensions
    {
        /// <summary>
        /// Adds MassTransit testing services (for development/testing only)
        /// </summary>
        public static IServiceCollection AddMassTransitTesting(this IServiceCollection services)
        {
            services.AddScoped<IMassTransitTestService, MassTransitTestService>();
            return services;
        }
    }
}