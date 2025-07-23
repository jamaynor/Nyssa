using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nyssa.Mcp.Server.Configuration;
using Nyssa.Mcp.Server.Services.RbacMessageHandlers;

namespace Nyssa.Mcp.Server.Tests
{
    /// <summary>
    /// Tests that verify the MassTransit and database configuration is properly set up
    /// </summary>
    public class ConfigurationTests
    {
        [Fact]
        public void MassTransitConfiguration_ShouldBindFromConfiguration()
        {
            // Arrange
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MassTransit:Transport"] = "RabbitMQ",
                    ["MassTransit:Retry:RetryLimit"] = "5",
                    ["MassTransit:CircuitBreaker:TripThreshold"] = "10",
                    ["MassTransit:RabbitMQ:Host"] = "test-host",
                    ["MassTransit:RabbitMQ:Port"] = "5673"
                })
                .Build();

            // Act
            var options = configuration.GetSection(MassTransitOptions.SectionName).Get<MassTransitOptions>();

            // Assert
            options.Should().NotBeNull();
            options!.Transport.Should().Be("RabbitMQ");
            options.Retry.RetryLimit.Should().Be(5);
            options.CircuitBreaker.TripThreshold.Should().Be(10);
            options.RabbitMQ.Host.Should().Be("test-host");
            options.RabbitMQ.Port.Should().Be(5673);
        }

        [Fact]
        public void DatabaseConfiguration_ShouldBindFromConfiguration()
        {
            // Arrange
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:ConnectionString"] = "Host=localhost;Database=test;Username=test;Password=test",
                    ["Database:Pool:MinPoolSize"] = "10",
                    ["Database:Pool:MaxPoolSize"] = "100",
                    ["Database:Timeout:CommandTimeout"] = "00:01:00"
                })
                .Build();

            // Act
            var options = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>();

            // Assert
            options.Should().NotBeNull();
            options!.ConnectionString.Should().Be("Host=localhost;Database=test;Username=test;Password=test");
            options.Pool.MinPoolSize.Should().Be(10);
            options.Pool.MaxPoolSize.Should().Be(100);
            options.Timeout.CommandTimeout.Should().Be(TimeSpan.FromMinutes(1));
        }

        [Fact]
        public void MassTransitOptions_ShouldHaveCorrectDefaults()
        {
            // Arrange & Act
            var options = new MassTransitOptions();

            // Assert
            options.Transport.Should().Be("InMemory");
            options.RabbitMQ.Should().NotBeNull();
            options.RabbitMQ.Host.Should().Be("localhost");
            options.RabbitMQ.Port.Should().Be(5672);
            options.RabbitMQ.Username.Should().Be("guest");
            options.RabbitMQ.Password.Should().Be("guest");
            options.RabbitMQ.PrefetchCount.Should().Be(16);
            options.Retry.Should().NotBeNull();
            options.Retry.RetryLimit.Should().Be(3);
            options.CircuitBreaker.Should().NotBeNull();
            options.CircuitBreaker.TripThreshold.Should().Be(5);
        }

        [Fact]
        public void DatabaseOptions_ShouldHaveCorrectDefaults()
        {
            // Arrange & Act
            var options = new DatabaseOptions();

            // Assert
            options.ConnectionString.Should().BeEmpty();
            options.Pool.Should().NotBeNull();
            options.Pool.MinPoolSize.Should().Be(5);
            options.Pool.MaxPoolSize.Should().Be(50);
            options.Timeout.Should().NotBeNull();
            options.Timeout.CommandTimeout.Should().Be(TimeSpan.FromSeconds(30));
            options.Timeout.ConnectionTimeout.Should().Be(TimeSpan.FromSeconds(15));
            options.HealthCheck.Should().NotBeNull();
            options.HealthCheck.Enabled.Should().BeTrue();
            options.HealthCheck.Interval.Should().Be(TimeSpan.FromMinutes(1));
        }

        [Theory]
        [InlineData("InMemory")]
        [InlineData("RabbitMQ")]
        public void MassTransitOptions_ShouldSupportBothTransports(string transport)
        {
            // Arrange
            var options = new MassTransitOptions
            {
                Transport = transport
            };

            // Act & Assert
            options.Transport.Should().Be(transport);
        }

        [Fact]
        public void ConfigurationSectionNames_ShouldBeCorrect()
        {
            // Assert
            MassTransitOptions.SectionName.Should().Be("MassTransit");
            DatabaseOptions.SectionName.Should().Be("Database");
        }
    }
}