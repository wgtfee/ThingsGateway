//------------------------------------------------------------------------------
//  此代码版权声明为全文件覆盖，如有原作者特别声明，会在下方手动补充
//  此代码版权（除特别声明外的代码）归作者本人Diego所有
//  源代码使用协议遵循本仓库的开源协议及附加协议
//  Gitee源代码仓库：https://gitee.com/diego2098/ThingsGateway
//  Github源代码仓库：https://github.com/kimdiego2098/ThingsGateway
//  使用文档：https://thingsgateway.cn/
//  QQ群：605534569
//------------------------------------------------------------------------------

using ThingsGateway.NewLife;

namespace ThingsGateway.Foundation;

/// <summary>
/// TCP服务器
/// </summary>
/// <typeparam name="TClient"></typeparam>
public abstract class TcpServiceChannelBase<TClient> : TcpService<TClient>, ITcpService<TClient> where TClient : TcpSessionClientChannel, new()
{

    ~TcpServiceChannelBase()
    {
        this.SafeDispose();
    }
    /// <inheritdoc/>
    public ConcurrentList<IDevice> Collects { get; } = new();

    public async Task ClientDisposeAsync(string id)
    {
        if (this.TryGetClient(id, out var client))
        {
            //if (ShutDownEnable)
            //    await client.ShutdownAsync(System.Net.Sockets.SocketShutdown.Both).ConfigureAwait(false);
            await client.CloseAsync().ConfigureAwait(false);
            client.SafeDispose();
        }
    }

    private readonly WaitLock _connectLock = new WaitLock(nameof(TcpServiceChannelBase<TClient>));
    /// <inheritdoc/>
    public virtual async Task StartAsync()
    {
        if (ServerState != ServerState.Running)
        {
            try
            {
                await _connectLock.WaitAsync().ConfigureAwait(false);

                if (ServerState != ServerState.Running)
                {
                    if (ServerState != ServerState.Stopped)
                    {
                        await base.StopAsync().ConfigureAwait(false);
                    }

                    //await SetupAsync(Config.Clone()).ConfigureAwait(false);
                    await base.StartAsync().ConfigureAwait(false);
                    if (ServerState == ServerState.Running)
                    {
                        Logger?.Info($"{Monitors.FirstOrDefault()?.Option.IpHost}{AppResource.ServiceStarted}");
                    }
                }
            }
            finally
            {
                var cts = m_transport;
                m_transport = new();
                cts?.SafeCancel();
                cts?.SafeDispose();
                _connectLock.Release();
            }
        }
    }

    /// <inheritdoc/>
    public override async Task<Result> StopAsync(CancellationToken token)
    {
        if (Monitors.Any())
        {
            try
            {
                await _connectLock.WaitAsync(token).ConfigureAwait(false);
                if (Monitors.Any())
                {
                    await ClearAsync().ConfigureAwait(false);
                    var iPHost = Monitors.FirstOrDefault()?.Option.IpHost;
                    var result = await base.StopAsync(token).ConfigureAwait(false);
                    if (!Monitors.Any())
                        Logger?.Info($"{iPHost}{AppResource.ServiceStoped}");
                    return result;
                }
            }
            finally
            {
                var cts = m_transport;
                m_transport = null;
                cts?.SafeCancel();
                cts?.SafeDispose();
                _connectLock.Release();
            }
        }
        else
        {
            var result = await base.StopAsync(token).ConfigureAwait(false);
            return result;
        }
        return Result.Success;
    }

    public CancellationToken ClosedToken => this.m_transport == null ? new CancellationToken(true) : this.m_transport.Token;
    private CancellationTokenSource m_transport;

    protected override void SafetyDispose(bool disposing)
    {
        m_transport?.SafeCancel();
        m_transport?.SafeDispose();
        m_transport = null;
        base.SafetyDispose(disposing);
    }
    /// <inheritdoc/>
    protected override Task OnTcpClosed(TClient socketClient, ClosedEventArgs e)
    {
        Logger?.Info($"{socketClient} Closed{(e.Message.IsNullOrEmpty() ? string.Empty : $"-{e.Message}")}");
        return base.OnTcpClosed(socketClient, e);
    }

    /// <inheritdoc/>
    protected override Task OnTcpClosing(TClient socketClient, ClosingEventArgs e)
    {
        Logger?.Trace($"{socketClient} Closing{(e.Message.IsNullOrEmpty() ? string.Empty : $"-{e.Message}")}");
        return base.OnTcpClosing(socketClient, e);
    }

    /// <inheritdoc/>
    protected override Task OnTcpConnected(TClient socketClient, ConnectedEventArgs e)
    {
        Logger?.Info($"{socketClient}  Connected");
        return base.OnTcpConnected(socketClient, e);
    }

    /// <inheritdoc/>
    protected override Task OnTcpConnecting(TClient socketClient, ConnectingEventArgs e)
    {
        Logger?.Trace($"{socketClient}  Connecting");
        return base.OnTcpConnecting(socketClient, e);
    }
}

/// <summary>
/// Tcp服务器通道
/// </summary>
public class TcpServiceChannel<TClient> : TcpServiceChannelBase<TClient>, IChannel, ITcpServiceChannel where TClient : TcpSessionClientChannel, IClientChannel, IChannel, new()
{
    /// <inheritdoc/>
    public TcpServiceChannel(IChannelOptions channelOptions)
    {
        ChannelOptions = channelOptions;
    }
    public override TouchSocketConfig Config => base.Config ?? ChannelOptions.Config;

    /// <inheritdoc/>
    public ChannelReceivedEventHandler ChannelReceived { get; } = new();

    /// <inheritdoc/>
    public IChannelOptions ChannelOptions { get; }

    /// <inheritdoc/>
    public ChannelTypeEnum ChannelType => ChannelOptions.ChannelType;

    /// <inheritdoc/>
    public bool Online => ServerState == ServerState.Running;

    /// <inheritdoc/>
    public ChannelEventHandler Started { get; } = new();

    /// <inheritdoc/>
    public ChannelEventHandler Starting { get; } = new();

    /// <inheritdoc/>
    public ChannelEventHandler Stoped { get; } = new();
    /// <inheritdoc/>
    public ChannelEventHandler Stoping { get; } = new();

    /// <inheritdoc/>
    public Task<Result> CloseAsync(string msg, CancellationToken token)
    {
        return StopAsync(token);
    }

    /// <inheritdoc/>
    public Task ConnectAsync(CancellationToken token = default)
    {
        if (token.IsCancellationRequested)
            return EasyTask.CompletedTask;

        return StartAsync();
    }
    /// <inheritdoc/>
    public override string? ToString()
    {
        return $"{ChannelOptions.BindUrl} {ChannelOptions.RemoteUrl}";
    }
    /// <inheritdoc/>
    protected override TClient NewClient()
    {
        var data = new TClient();
        data.ResetSign(MinSign, MaxSign);
        return data;
    }
    public int MinSign { get; private set; } = 0;
    public int MaxSign { get; private set; } = ushort.MaxValue;
    public void ResetSign(int minSign = 1, int maxSign = ushort.MaxValue - 1)
    {
        MinSign = minSign;
        MaxSign = maxSign;
    }
    /// <inheritdoc/>
    protected override async Task OnTcpClosing(TClient socketClient, ClosingEventArgs e)
    {
        await socketClient.OnChannelEvent(Stoping).ConfigureAwait(false);
        await base.OnTcpClosing(socketClient, e).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    protected override async Task OnTcpClosed(TClient socketClient, ClosedEventArgs e)
    {
        await socketClient.OnChannelEvent(Stoped).ConfigureAwait(false);
        await base.OnTcpClosed(socketClient, e).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    protected override async Task OnTcpConnected(TClient socketClient, ConnectedEventArgs e)
    {
        await socketClient.OnChannelEvent(Started).ConfigureAwait(false);
        await base.OnTcpConnected(socketClient, e).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    protected override async Task OnTcpConnecting(TClient socketClient, ConnectingEventArgs e)
    {
        await socketClient.OnChannelEvent(Starting).ConfigureAwait(false);
        await base.OnTcpConnecting(socketClient, e).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    protected override async Task OnTcpReceived(TClient socketClient, ReceivedDataEventArgs e)
    {
        await base.OnTcpReceived(socketClient, e).ConfigureAwait(false);

        if (e.Handled)
            return;

        await socketClient.OnChannelReceivedEvent(e, ChannelReceived).ConfigureAwait(false);

    }



    IEnumerable<TcpSessionClientChannel> ITcpServiceChannel.Clients => base.Clients;


    protected override void ClientInitialized(TClient client)
    {
        client.ChannelOptions = ChannelOptions;

        client.WaitLock = new NewLife.WaitLock(nameof(TcpServiceChannelBase<TClient>), ChannelOptions.WaitLock.MaxCount);

        base.ClientInitialized(client);
    }

    public bool TryGetClient(string id, out TcpSessionClientChannel client)
    {
        bool result = ServiceExtension.TryGetClient<TClient>(this, id, out var tclient);
        client = tclient;
        return result;
    }
}
