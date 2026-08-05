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
/// Udp通道
/// </summary>
public class UdpSessionChannel : UdpSession, IClientChannel
{
    ~UdpSessionChannel()
    {
        this.SafeDispose();
    }
    private readonly WaitLock _connectLock = new WaitLock(nameof(UdpSessionChannel));

    /// <inheritdoc/>
    public UdpSessionChannel(IChannelOptions channelOptions)
    {
        ChannelOptions = channelOptions;
        ResetSign();
    }
    public override TouchSocketConfig Config => base.Config ?? ChannelOptions.Config;
    /// <inheritdoc/>
    public void SetDataHandlingAdapterLogger(ILog log)
    {
        if (DataHandlingAdapter is IDeviceDataHandleAdapter handleAdapter)
        {
            handleAdapter.Logger = log;
        }
    }

    /// <inheritdoc/>
    public void SetDataHandlingAdapter(DataHandlingAdapter adapter)
    {
        if (adapter is UdpDataHandlingAdapter udpDataHandlingAdapter)
            SetAdapter(udpDataHandlingAdapter);

    }


    public void ResetSign(int minSign = 1, int maxSign = ushort.MaxValue - 1)
    {
        var pool = WaitHandlePool;
        WaitHandlePool = new WaitHandlePool<MessageBase>(minSign, maxSign);
        pool?.CancelAll();
    }

    /// <inheritdoc/>
    public ChannelReceivedEventHandler ChannelReceived { get; } = new();

    /// <inheritdoc/>
    public IChannelOptions ChannelOptions { get; }

    /// <inheritdoc/>
    public ChannelTypeEnum ChannelType => ChannelOptions.ChannelType;

    /// <inheritdoc/>
    public ConcurrentList<IDevice> Collects { get; } = new();

    /// <inheritdoc/>
    public bool Online => ServerState == ServerState.Running;

    /// <inheritdoc/>
    public DataHandlingAdapter ReadOnlyDataHandlingAdapter => DataHandlingAdapter;

    /// <inheritdoc/>
    public ChannelEventHandler Started { get; } = new();

    /// <inheritdoc/>
    public ChannelEventHandler Starting { get; } = new();

    /// <inheritdoc/>
    public ChannelEventHandler Stoped { get; } = new();
    /// <inheritdoc/>
    public ChannelEventHandler Stoping { get; } = new();

    /// <summary>
    /// 等待池
    /// </summary>
    public WaitHandlePool<MessageBase> WaitHandlePool { get; internal set; } = new(1, ushort.MaxValue - 1);

    /// <inheritdoc/>
    public WaitLock WaitLock => ChannelOptions.WaitLock;
    public virtual WaitLock GetLock(string key) => WaitLock;



    /// <inheritdoc/>
    public Task<Result> CloseAsync(string msg, CancellationToken token)
    {
        return StopAsync(token);
    }

    /// <inheritdoc/>
    public Task ConnectAsync(CancellationToken token = default)
    {
        if (token.IsCancellationRequested)
            return EasyTask.CompletedTask; ;
        return StartAsync();
    }


    public CancellationToken ClosedToken => this.m_transport == null ? new CancellationToken(true) : this.m_transport.Token;
    private CancellationTokenSource m_transport;
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
                    await this.OnChannelEvent(Starting).ConfigureAwait(false);
                    await base.StartAsync().ConfigureAwait(false);
                    if (ServerState == ServerState.Running)
                    {
                        Logger?.Info($"{Monitor.IPHost}{AppResource.ServiceStarted}");
                        await this.OnChannelEvent(Started).ConfigureAwait(false);
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
        if (Monitor != null)
        {
            try
            {
                await _connectLock.WaitAsync(token).ConfigureAwait(false);
                if (Monitor != null)
                {
                    await this.OnChannelEvent(Stoping).ConfigureAwait(false);
                    var result = await base.StopAsync(token).ConfigureAwait(false);
                    if (Monitor == null)
                    {
                        await this.OnChannelEvent(Stoped).ConfigureAwait(false);
                        Logger?.Info($"{AppResource.ServiceStoped}");
                    }
                    return result;
                }
                else
                {
                    var result = await base.StopAsync(token).ConfigureAwait(false);
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
    }

    /// <inheritdoc/>
    public override string? ToString()
    {
        return $"{ChannelOptions.BindUrl} {ChannelOptions.RemoteUrl}";
    }

    /// <inheritdoc/>
    protected override async Task OnUdpReceived(UdpReceivedDataEventArgs e)
    {
        await base.OnUdpReceived(e).ConfigureAwait(false);

        if (e.Handled)
            return;

        await this.OnChannelReceivedEvent(e, ChannelReceived).ConfigureAwait(false);

    }

    /// <inheritdoc/>
    protected override void SafetyDispose(bool disposing)
    {
        m_transport?.SafeCancel();
        m_transport?.SafeDispose();
        m_transport = null;
        WaitHandlePool?.CancelAll();
        base.SafetyDispose(disposing);
    }
}
