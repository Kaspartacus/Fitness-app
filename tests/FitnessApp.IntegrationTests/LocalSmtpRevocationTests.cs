using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FitnessApp.Application.Authentication;
using FitnessApp.Application.Email;
using FitnessApp.Infrastructure;
using FitnessApp.Infrastructure.Email;
using MailKit.Security;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace FitnessApp.IntegrationTests;

public sealed class LocalSmtpRevocationTests
{
    [Theory]
    [InlineData("Production", "https://localhost", "https://localhost:7192")]
    [InlineData("Staging", "https://localhost", "https://localhost:7192")]
    [InlineData("Testing", "https://localhost", "https://localhost:7192")]
    [InlineData("Development", "https://public.example.test", "https://localhost:7192")]
    [InlineData("Development", "https://localhost", "http://0.0.0.0:5192")]
    [InlineData("Development", "https://localhost", "http://[::]:5192")]
    [InlineData("Development", "https://localhost", "http://192.168.1.2:5192")]
    [InlineData("Development", "https://localhost", "https://localhost:7192;http://*:5192")]
    [InlineData("Development", "https://localhost", "")]
    public async Task BypassRefusesNonLocalOrUnknownHostingBeforeConnecting(
        string environment, string origin, string listeners)
    {
        using var services = CreateServices(true, environment, origin, listeners, 587);
        var sender = services.GetRequiredService<IEmailSender>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(Message(), CancellationToken.None));
    }

    [Fact]
    public async Task StartupRejectsBypassOutsideDevelopmentEvenWhenPickupIsSelected()
    {
        using var factory = new AuthWebApplicationFactory(configurationOverrides:
            new Dictionary<string, string?>
            {
                ["Smtp:AllowLocalDevelopmentRevocationBypass"] = "true",
                ["PublicApp:BaseUrl"] = "https://localhost"
            });
        await Assert.ThrowsAsync<OptionsValidationException>(factory.InitializeDatabaseAsync);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LocalBypassStillRejectsUntrustedTlsCertificates(bool bypass)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        request.CertificateExtensions.Add(names.Build());
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(5));
        var handshake = ServeStartTlsAsync(listener, certificate, timeout.Token);
        using var services = CreateServices(bypass, "Development", "https://localhost", "https://localhost:7192", port);
        var sender = services.GetRequiredService<IEmailSender>();
        await Assert.ThrowsAsync<SslHandshakeException>(() => sender.SendAsync(Message(), timeout.Token));
        await handshake;
        Assert.Equal(bypass, services.GetRequiredService<TestLogSink>().Entries.Any(entry => entry.Contains("|1312|")));
    }

    private static async Task ServeStartTlsAsync(TcpListener listener, X509Certificate2 certificate, CancellationToken cancellationToken)
    {
        using var socket = await listener.AcceptTcpClientAsync(cancellationToken);
        await using var stream = socket.GetStream();
        using var reader = new StreamReader(stream, leaveOpen: true);
        await using var writer = new StreamWriter(stream, leaveOpen: true) { AutoFlush = true, NewLine = "\r\n" };
        await writer.WriteLineAsync("220 localhost test SMTP");
        Assert.StartsWith("EHLO ", await reader.ReadLineAsync(cancellationToken));
        await writer.WriteLineAsync("250-localhost\r\n250 STARTTLS");
        Assert.Equal("STARTTLS", await reader.ReadLineAsync(cancellationToken));
        await writer.WriteLineAsync("220 Start TLS");
        await using var tls = new SslStream(stream, leaveInnerStreamOpen: true);
        try
        {
            await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = certificate,
                ClientCertificateRequired = false
            }, cancellationToken);
        }
        catch (Exception exception) when (exception is System.Security.Authentication.AuthenticationException or IOException)
        {
            // The client must reject this untrusted certificate before SMTP authentication.
        }
    }

    private static ServiceProvider CreateServices(bool bypass, string environment, string origin, string listeners, int port)
    {
        var services = new ServiceCollection();
        var logs = new TestLogSink();
        services.AddSingleton(logs);
        services.AddLogging(builder => builder.AddProvider(logs));
        services.AddSingleton<IHostEnvironment>(new TestEnvironment { EnvironmentName = environment });
        services.AddSingleton<IServer>(new TestServer(listeners));
        services.Configure<PublicAppOptions>(options => options.BaseUrl = origin);
        services.Configure<SmtpOptions>(options =>
        {
            options.Host = "localhost";
            options.Port = port;
            options.FromEmail = "sender@example.test";
            options.FromName = "Test sender";
            options.AllowLocalDevelopmentRevocationBypass = bypass;
        });
        services.AddEmailDelivery(usePickupDirectory: false);
        return services.BuildServiceProvider();
    }

    private static EmailMessage Message() => new("test-message", "recipient@example.test", "Test", "Test", "<p>Test</p>");

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class TestServer : IServer
    {
        public TestServer(string listeners)
        {
            var addresses = new ServerAddressesFeature();
            foreach (var address in listeners.Split(';', StringSplitOptions.RemoveEmptyEntries))
                addresses.Addresses.Add(address);
            Features.Set<IServerAddressesFeature>(addresses);
        }
        public IFeatureCollection Features { get; } = new FeatureCollection();
        public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken cancellationToken) where TContext : notnull => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void Dispose() { }
    }
}
