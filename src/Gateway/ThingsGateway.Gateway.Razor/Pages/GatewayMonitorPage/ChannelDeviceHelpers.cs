using Mapster;

using ThingsGateway.Gateway.Application;
//------------------------------------------------------------------------------
//  此代码版权声明为全文件覆盖，如有原作者特别声明，会在下方手动补充
//  此代码版权（除特别声明外的代码）归作者本人Diego所有
//  源代码使用协议遵循本仓库的开源协议及附加协议
//  Gitee源代码仓库：https://gitee.com/diego2098/ThingsGateway
//  Github源代码仓库：https://github.com/kimdiego2098/ThingsGateway
//  使用文档：https://thingsgateway.cn/
//  QQ群：605534569
//------------------------------------------------------------------------------

namespace ThingsGateway.Gateway.Razor
{
    internal static class ChannelDeviceHelpers
    {

        internal static Channel GetChannelModel(ItemChangedType itemChangedType, ChannelDeviceTreeItem channelDeviceTreeItem)
        {
            Channel oneModel = null;
            if (channelDeviceTreeItem.TryGetChannelRuntime(out var channelRuntime))
            {
                oneModel = channelRuntime.Adapt<Channel>();
                if (itemChangedType == ItemChangedType.Add)
                {
                    oneModel.Id = 0;
                    oneModel.Name = $"{oneModel.Name}-Copy";
                }
            }
            else if (channelDeviceTreeItem.TryGetDeviceRuntime(out var deviceRuntime))
            {
                oneModel = deviceRuntime.ChannelRuntime?.Adapt<Channel>() ?? new();
                if (itemChangedType == ItemChangedType.Add)
                {
                    oneModel.Id = 0;
                    oneModel.Name = $"{oneModel.Name}-Copy";
                }
            }
            else if (channelDeviceTreeItem.TryGetPluginName(out var pluginName))
            {
                oneModel = new();
                oneModel.PluginName = pluginName;
            }
            else if (channelDeviceTreeItem.TryGetPluginType(out var pluginType))
            {
                oneModel = new();
            }
            return oneModel;
        }
        internal static Device GetDeviceModel(ItemChangedType itemChangedType, ChannelDeviceTreeItem channelDeviceTreeItem)
        {
            Device oneModel = null;
            if (channelDeviceTreeItem.TryGetDeviceRuntime(out var deviceRuntime))
            {
                oneModel = deviceRuntime.Adapt<Device>();
                if (itemChangedType == ItemChangedType.Add)
                {
                    oneModel.Id = 0;
                    oneModel.Name = $"{oneModel.Name}-Copy";
                }
            }
            else if (channelDeviceTreeItem.TryGetChannelRuntime(out var channelRuntime))
            {
                oneModel = new();
                oneModel.Id = 0;
                oneModel.ChannelId = channelRuntime.Id;
            }
            else
            {
                oneModel = new();
                oneModel.Id = 0;
            }
            return oneModel;
        }

        internal static PluginTypeEnum? GetPluginType(ChannelDeviceTreeItem channelDeviceTreeItem)
        {
            PluginTypeEnum? pluginTypeEnum = null;
            if (channelDeviceTreeItem.TryGetPluginType(out var pluginType))
            {
                pluginTypeEnum = pluginType;
            }
            return pluginTypeEnum;
        }



        internal static async Task ShowCopy(Channel oneModel, Dictionary<Device, List<Variable>> deviceDict, string text, bool autoRestart, Func<Task> onsave, DialogService dialogService)
        {
            var op = new DialogOption()
            {
                IsScrolling = false,
                ShowMaximizeButton = true,
                Size = Size.ExtraLarge,
                Title = text,
                ShowFooter = false,
                ShowCloseButton = false,
            };

            op.Component = BootstrapDynamicComponent.CreateComponent<ChannelCopyComponent>(new Dictionary<string, object?>
        {
             {nameof(ChannelCopyComponent.OnSave), async (List<Channel> channels,Dictionary<Device,List<Variable>> devices) =>
            {

                await Task.Run(() =>GlobalData.ChannelRuntimeService.CopyAsync(channels,devices,autoRestart, default));
                if(onsave!=null)
                    await onsave();

            }},
            {nameof(ChannelCopyComponent.Model),oneModel },
            {nameof(ChannelCopyComponent.Devices),deviceDict },
        });

            await dialogService.Show(op);
        }






    }
}