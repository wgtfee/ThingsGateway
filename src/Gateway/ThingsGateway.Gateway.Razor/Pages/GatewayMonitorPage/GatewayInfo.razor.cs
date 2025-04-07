//------------------------------------------------------------------------------
//  此代码版权声明为全文件覆盖，如有原作者特别声明，会在下方手动补充
//  此代码版权（除特别声明外的代码）归作者本人Diego所有
//  源代码使用协议遵循本仓库的开源协议及附加协议
//  Gitee源代码仓库：https://gitee.com/diego2098/ThingsGateway
//  Github源代码仓库：https://github.com/kimdiego2098/ThingsGateway
//  使用文档：https://thingsgateway.cn/
//  QQ群：605534569
//------------------------------------------------------------------------------

using ThingsGateway.Gateway.Application;

namespace ThingsGateway.Gateway.Razor;

public partial class GatewayInfo
{
    [Parameter]
    public ChannelDeviceTreeItem SelectModel { get; set; } = new() { ChannelDevicePluginType = ChannelDevicePluginTypeEnum.PluginType, PluginType = null };

    [Parameter]
    public IEnumerable<VariableRuntime> VariableRuntimes { get; set; } = Enumerable.Empty<VariableRuntime>();

    [Parameter]
    public IEnumerable<ChannelRuntime> ChannelRuntimes { get; set; } = Enumerable.Empty<ChannelRuntime>();

    [Parameter]
    public IEnumerable<DeviceRuntime> DeviceRuntimes { get; set; } = Enumerable.Empty<DeviceRuntime>();
    [Parameter]
    public long ShowChannelRuntime { get; set; }
    [Parameter]
    public long ShowDeviceRuntime { get; set; }
    [Parameter]
    public ShowTypeEnum? ShowType { get; set; }
    [Parameter]
    public bool AutoRestartThread { get; set; } = true;

}
