using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using FitnessApp.Client.Authentication;
using FitnessApp.Client.Diary;
using FitnessApp.Client.Layout;
using FitnessApp.Contracts.Diary;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using DiaryPage = FitnessApp.Client.Pages.Diary;

namespace FitnessApp.IntegrationTests;

public sealed class DiaryPrivacyTests
{
    [Theory]
    [InlineData(false, false, false, "protection", "diary-new", "diary-confirm")]
    [InlineData(true, true, false, "recover", "diary-unlock", "diary-account-password")]
    [InlineData(true, true, true, "protection", "diary-current", "diary-new", "diary-confirm", "diary-text")]
    public async Task ProductionPageAndDialogsShareTheObservedPrivacyRootInEveryState(
        bool initialized, bool protection, bool unlocked, string dialog, params string[] expectedFields)
    {
        var today = new DateOnly(2026, 10, 22);
        var lockRequests = 0;
        using var http = new HttpClient(new SyntheticHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/state")) return Json(new DiaryStatusResponse(initialized, protection, unlocked, today, "Europe/Copenhagen"));
            if (path.Contains("/entries/")) return Json(new DiaryEntryResponse(today, "Synthetic private entry", Guid.NewGuid()));
            if (path.Contains("/dates/")) return Json(Array.Empty<DiaryDateResponse>());
            if (path.EndsWith("/lock")) lockRequests++;
            return Json(true);
        })) { BaseAddress = new("https://localhost/") };
        using var api = new DiaryClient(new SyntheticFactory(http), new InMemoryAuthenticationStateProvider());
        var javaScript = new RecordingJavaScript();
        var navigation = new PrivacyNavigationManager();
        using var services = new ServiceCollection()
            .AddSingleton<IJSRuntime>(javaScript)
            .AddSingleton<NavigationManager>(navigation)
            .BuildServiceProvider();
        var page = new DiaryPage();
        Inject(page, "DiaryApi", api); Inject(page, "JS", javaScript); Inject(page, "Navigation", navigation);
        Set(page, "dialog", dialog);
        foreach (var field in new[] { "code", "currentCode", "newCode", "confirmation", "accountPassword" })
            Set(page, field, "Synthetic secret");
        var renderer = new PrivacyRenderer(services);
        try
        {
            var componentId = await renderer.Dispatcher.InvokeAsync(() => renderer.Attach(page));
            await renderer.Dispatcher.InvokeAsync(() => renderer.RenderAsync(componentId));
            var snapshot = await renderer.Dispatcher.InvokeAsync(() => renderer.Inspect(componentId));
            var root = Assert.Single(snapshot.Elements, element => element.Attributes.ContainsKey("data-diary-privacy-root"));
            var observed = Assert.Single(javaScript.Starts);
            Assert.Equal(root.ReferenceId, observed.Id);
            Assert.False(string.IsNullOrEmpty(observed.Id));
            Assert.Equal(5, snapshot.ModalRoots.Count);
            Assert.All(snapshot.ModalRoots, insideRoot => Assert.True(insideRoot));
            Assert.True(Assert.Single(snapshot.Elements, element => element.Attributes.GetValueOrDefault("role") == "dialog").InsideRoot);
            foreach (var id in expectedFields)
            {
                var field = Assert.Single(snapshot.Elements, element => element.Attributes.GetValueOrDefault("id") == id);
                Assert.True(field.InsideRoot);
            }
            Assert.All(snapshot.Elements.Where(element => element.Name is "input" or "textarea"), element => Assert.True(element.InsideRoot));

            // Re-rendering a dialog or input must not restart the absolute inactivity deadline.
            await renderer.Dispatcher.InvokeAsync(() => renderer.RenderAsync(componentId));
            Assert.Single(javaScript.Starts);
            // The production subscription replaces the hidden shell when locking completes.
            await renderer.Dispatcher.InvokeAsync(() => api.Clear());
            await renderer.Dispatcher.InvokeAsync(() => renderer.RenderAsync(componentId));
            var replacement = await renderer.Dispatcher.InvokeAsync(() => renderer.Inspect(componentId));
            var newRoot = Assert.Single(replacement.Elements, element => element.Attributes.ContainsKey("data-diary-privacy-root"));
            Assert.Equal(2, javaScript.Starts.Count);
            Assert.NotEqual(observed.Id, javaScript.Starts[1].Id);
            Assert.Equal(newRoot.ReferenceId, javaScript.Starts[1].Id);
        }
        finally { await renderer.DisposeRendererAsync(); }

        Assert.Contains("fitnessDiary.stop", javaScript.Calls);
        var locksAfterDisposal = lockRequests;
        await page.IdleLock(); // A callback already queued in the disposed component cannot clear a later grant.
        Assert.Equal(locksAfterDisposal, lockRequests);
    }

    private static void Inject(DiaryPage page, string name, object value) =>
        typeof(DiaryPage).GetProperty(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(page, value);
    private static void Set(DiaryPage page, string name, object value) =>
        typeof(DiaryPage).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(page, value);
    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    private sealed class SyntheticFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
    private sealed class SyntheticHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
    private sealed class RecordingJavaScript : IJSRuntime
    {
        internal List<string> Calls { get; } = [];
        internal List<ElementReference> Starts { get; } = [];
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Calls.Add(identifier);
            if (identifier == "fitnessDiary.start") Starts.Add((ElementReference)args![1]!);
            return ValueTask.FromResult(default(TValue)!);
        }
    }
    private sealed class PrivacyNavigationManager : NavigationManager
    {
        public PrivacyNavigationManager() => Initialize("https://localhost/", "https://localhost/dagbog");
        protected override void NavigateToCore(string uri, bool forceLoad) => Uri = ToAbsoluteUri(uri).ToString();
        protected override void SetNavigationLockState(bool value) { }
    }
}

// Inspect the actual production component tree, including the ModalDialog child render trees.
// No presentation or lifecycle methods are replaced by a probe component.
#pragma warning disable BL0006
internal sealed class PrivacyRenderer(IServiceProvider services) : Renderer(services, NullLoggerFactory.Instance)
{
    public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
    internal int Attach(IComponent component) => AssignRootComponentId(component);
    internal Task RenderAsync(int componentId) => RenderRootComponentAsync(componentId);
    internal ValueTask DisposeRendererAsync() => DisposeAsync();
    internal PrivacySnapshot Inspect(int componentId)
    {
        var result = new PrivacySnapshot();
        VisitComponent(componentId, false);
        return result;

        void VisitComponent(int id, bool insideRoot)
        {
            var frames = GetCurrentRenderTreeFrames(id);
            VisitFrames(frames.Array, 0, frames.Count, insideRoot);
        }
        void VisitFrames(RenderTreeFrame[] frames, int start, int end, bool insideRoot)
        {
            for (var index = start; index < end;)
            {
                var frame = frames[index];
                if (frame.FrameType == RenderTreeFrameType.Element)
                {
                    var subtreeEnd = index + frame.ElementSubtreeLength;
                    var attributes = new Dictionary<string, string?>();
                    for (var attribute = index + 1; attribute < subtreeEnd && frames[attribute].FrameType == RenderTreeFrameType.Attribute; attribute++)
                        attributes[frames[attribute].AttributeName] = frames[attribute].AttributeValue?.ToString();
                    var underRoot = insideRoot || attributes.ContainsKey("data-diary-privacy-root");
                    string? referenceId = null;
                    for (var child = index + 1; child < subtreeEnd;)
                    {
                        var candidate = frames[child];
                        if (candidate.FrameType == RenderTreeFrameType.ElementReferenceCapture) referenceId = candidate.ElementReferenceCaptureId;
                        child += candidate.FrameType switch
                        {
                            RenderTreeFrameType.Element => candidate.ElementSubtreeLength,
                            RenderTreeFrameType.Component => candidate.ComponentSubtreeLength,
                            RenderTreeFrameType.Region => candidate.RegionSubtreeLength,
                            _ => 1
                        };
                    }
                    result.Elements.Add(new(frame.ElementName, attributes, underRoot, referenceId));
                    VisitFrames(frames, index + 1, subtreeEnd, underRoot);
                    index = subtreeEnd;
                }
                else if (frame.FrameType == RenderTreeFrameType.Component)
                {
                    if (frame.ComponentType == typeof(ModalDialog)) result.ModalRoots.Add(insideRoot);
                    VisitComponent(frame.ComponentId, insideRoot);
                    index += frame.ComponentSubtreeLength;
                }
                else if (frame.FrameType == RenderTreeFrameType.Region)
                {
                    VisitFrames(frames, index + 1, index + frame.RegionSubtreeLength, insideRoot);
                    index += frame.RegionSubtreeLength;
                }
                else index++;
            }
        }
    }
    protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.CompletedTask;
    protected override void HandleException(Exception exception) => throw new InvalidOperationException("Synthetic diary privacy render failed.", exception);
}
internal sealed class PrivacySnapshot
{
    internal List<PrivacyElement> Elements { get; } = [];
    internal List<bool> ModalRoots { get; } = [];
}
internal sealed record PrivacyElement(string Name, Dictionary<string, string?> Attributes, bool InsideRoot, string? ReferenceId);
#pragma warning restore BL0006
