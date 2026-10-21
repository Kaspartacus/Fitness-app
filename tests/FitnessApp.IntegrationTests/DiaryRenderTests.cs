using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using FitnessApp.Client.Authentication;
using FitnessApp.Client.Diary;
using FitnessApp.Contracts.Diary;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using DiaryPage = FitnessApp.Client.Pages.Diary;

namespace FitnessApp.IntegrationTests;

public sealed class DiaryRenderTests
{
    [Theory]
    [InlineData("WriteToday")]
    [InlineData("RequestSave")]
    public async Task AsyncDiaryEventsRenderCompletionWithoutAnotherUserEvent(string handler)
    {
        var today = new DateOnly(2026, 10, 22);
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new DelayedHandler(async request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (handler == "WriteToday" && path.EndsWith("/state") ||
                handler == "RequestSave" && request.Method == HttpMethod.Put)
            {
                requested.TrySetResult();
                return await pending.Task;
            }
            if (path.Contains("/entries/")) return Json(new DiaryEntryResponse(today, "Synthetic completed page", Guid.NewGuid()));
            return Json(Array.Empty<DiaryDateResponse>());
        })) { BaseAddress = new("https://localhost/") };
        using var api = new DiaryClient(new Factory(http), new InMemoryAuthenticationStateProvider());
        var page = new DiaryPageProbe { EventMethod = handler };
        typeof(DiaryPage).GetProperty("DiaryApi", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(page, api);
        page.Set("state", new DiaryStatusResponse(true, false, true, today.AddDays(-1), "Europe/Copenhagen"));
        page.Set("selected", today); page.Set("month", new DateOnly(today.Year, today.Month, 1));
        page.Set("text", handler == "RequestSave" ? "Synthetic unsaved page" : "");
        page.Attach(); await page.RenderAsync();
        var clicked = page.ClickAsync();
        await requested.Task;
        Assert.Contains("Busy=True", page.Snapshot);
        var batchesBeforeCompletion = page.RenderHost.Batches;
        pending.SetResult(handler == "WriteToday"
            ? Json(new DiaryStatusResponse(true, false, true, today, "Europe/Copenhagen"))
            : Json(new DiaryEntryResponse(today, "Synthetic completed page", Guid.NewGuid())));
        await clicked;
        Assert.True(page.RenderHost.Batches > batchesBeforeCompletion);
        Assert.Contains("Busy=False", page.Snapshot);
        Assert.Contains("Text=Synthetic completed page", page.Snapshot);
        Assert.Contains("Saved=True", page.Snapshot);
        await page.DisposeRendererAsync();
    }

    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    private sealed class Factory(HttpClient client) : IHttpClientFactory { public HttpClient CreateClient(string name) => client; }
    private sealed class DelayedHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => response(request);
    }
}

// A tiny in-memory renderer exercises Blazor's real EventCallback completion renders. It does
// not start the application or browser. The probe uses production diary handlers and substitutes
// only the presentation/lifecycle to keep these tests independent of unrelated layout services.
#pragma warning disable BL0006
internal sealed class DiaryPageProbe : DiaryPage, IAsyncDisposable
{
    internal DiaryTestRenderer RenderHost { get; } = new();
    internal string EventMethod { get; set; } = "WriteToday";
    private int componentId;
    internal void Set(string name, object value) => typeof(DiaryPage).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(this, value);
    private T Read<T>(string name) => (T)typeof(DiaryPage).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(this)!;
    internal void Attach() => RenderHost.Dispatcher.InvokeAsync(() => componentId = RenderHost.Attach(this)).GetAwaiter().GetResult();
    internal Task RenderAsync() => RenderHost.Dispatcher.InvokeAsync(() => RenderHost.RenderAsync(componentId));
    internal ValueTask DisposeRendererAsync() => RenderHost.DisposeAsync();
    internal Task ClickAsync() => RenderHost.Dispatcher.InvokeAsync(() => RenderHost.DispatchEventAsync(RenderHost.ClickHandler(componentId), null, new MouseEventArgs()));
    internal string Snapshot => RenderHost.Dispatcher.InvokeAsync(() => RenderHost.Snapshot(componentId)).GetAwaiter().GetResult();
    protected override Task OnInitializedAsync() => Task.CompletedTask;
    protected override Task OnAfterRenderAsync(bool firstRender) => Task.CompletedTask;
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "button");
        builder.AddAttribute(1, "onclick", EventCallback.Factory.Create(this, ProductionEventAsync));
        builder.AddAttribute(2, "disabled", Read<bool>("busy"));
        builder.AddContent(3, $"Busy={Read<bool>("busy")};Text={Read<string>("text")};Saved={Read<bool>("saved")};Error={Read<string?>("error")};Dialog={Read<string?>("dialog")}");
        builder.CloseElement();
    }
    private Task ProductionEventAsync() => (Task)typeof(DiaryPage).GetMethod(EventMethod, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(this, null)!;
    // The probe never registers JS listeners or a production lifecycle subscription.
    ValueTask IAsyncDisposable.DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class DiaryTestRenderer() : Renderer(new ServiceCollection().BuildServiceProvider(), NullLoggerFactory.Instance)
{
    public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
    internal int Batches { get; private set; }
    internal int Attach(IComponent component) => AssignRootComponentId(component);
    internal Task RenderAsync(int componentId) => RenderRootComponentAsync(componentId);
    internal ulong ClickHandler(int componentId)
    {
        var frames = GetCurrentRenderTreeFrames(componentId);
        return frames.Array.Take(frames.Count).Single(frame => frame.FrameType == RenderTreeFrameType.Attribute && frame.AttributeName == "onclick").AttributeEventHandlerId;
    }
    internal string Snapshot(int componentId)
    {
        var frames = GetCurrentRenderTreeFrames(componentId);
        return string.Concat(frames.Array.Take(frames.Count).Where(frame => frame.FrameType == RenderTreeFrameType.Text).Select(frame => frame.TextContent));
    }
    protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) { Batches++; return Task.CompletedTask; }
    protected override void HandleException(Exception exception) => throw new InvalidOperationException("Synthetic diary render failed.", exception);
}
#pragma warning restore BL0006
