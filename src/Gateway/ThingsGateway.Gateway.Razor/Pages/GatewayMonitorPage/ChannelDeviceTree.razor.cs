//------------------------------------------------------------------------------
//  此代码版权声明为全文件覆盖，如有原作者特别声明，会在下方手动补充
//  此代码版权（除特别声明外的代码）归作者本人Diego所有
//  源代码使用协议遵循本仓库的开源协议及附加协议
//  Gitee源代码仓库：https://gitee.com/diego2098/ThingsGateway
//  Github源代码仓库：https://github.com/kimdiego2098/ThingsGateway
//  使用文档：https://thingsgateway.cn/
//  QQ群：605534569
//------------------------------------------------------------------------------

using Mapster;

using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;

using SqlSugar;

using ThingsGateway.Admin.Razor;
using ThingsGateway.Gateway.Application;
using ThingsGateway.NewLife;
using ThingsGateway.NewLife.Extension;
using ThingsGateway.NewLife.Json.Extension;

namespace ThingsGateway.Gateway.Razor;

public partial class ChannelDeviceTree
{
    SpinnerComponent Spinner;
    [Inject]
    [NotNull]
    protected BlazorAppContext? AppContext { get; set; }

    [Inject]
    [NotNull]
    private NavigationManager? NavigationManager { get; set; }

    public string RouteName => NavigationManager.ToBaseRelativePath(NavigationManager.Uri);

    protected bool AuthorizeButton(string operate)
    {
        return AppContext.IsHasButtonWithRole(RouteName, operate);
    }

    [Parameter]
    public EventCallback<ShowTypeEnum?> ShowTypeChanged { get; set; }
    [Parameter]
    public ShowTypeEnum? ShowType { get; set; }

    private async Task OnShowTypeChanged(ShowTypeEnum? showType)
    {
        ShowType = showType;
        if (showType != null && Module != null)
            await Module!.InvokeVoidAsync("saveShowType", ShowType);
        if (ShowTypeChanged.HasDelegate)
            await ShowTypeChanged.InvokeAsync(showType);
    }
    protected override async Task InvokeInitAsync()
    {
        await base.InvokeInitAsync();
        var showType = await Module!.InvokeAsync<ShowTypeEnum>("getShowType");
        await OnShowTypeChanged(showType);
    }

    [Parameter]
    public bool AutoRestartThread { get; set; }

    private static string GetClass(ChannelDeviceTreeItem item)
    {
        if (item.TryGetChannelRuntime(out var channelRuntime))
        {
            return channelRuntime.DeviceThreadManage != null ? "enable--text" : "disabled--text";

        }
        else if (item.TryGetDeviceRuntime(out var deviceRuntime))
        {
            if (deviceRuntime.Driver?.DeviceThreadManage != null)
            {
                if (deviceRuntime.DeviceStatus == DeviceStatusEnum.OnLine)
                {
                    return "green--text";
                }
                else
                {
                    return "red--text";
                }
            }
            else
            {
                return "disabled--text";
            }
        }
        return "enable--text";
    }

    [Inject]
    DialogService DialogService { get; set; }

    [Inject]
    WinBoxService WinBoxService { get; set; }



    [Inject]
    [NotNull]
    private IGatewayExportService? GatewayExportService { get; set; }

    #region 通道

    async Task EditChannel(ContextMenuItem item, object value, ItemChangedType itemChangedType)
    {
        var op = new DialogOption()
        {
            IsScrolling = false,
            ShowMaximizeButton = true,
            Size = Size.ExtraLarge,
            Title = item.Text,
            ShowFooter = false,
            ShowCloseButton = false,
        };

        if (value is not ChannelDeviceTreeItem channelDeviceTreeItem) return;
        PluginTypeEnum? pluginTypeEnum = ChannelDeviceHelpers.GetPluginType(channelDeviceTreeItem);
        var oneModel = ChannelDeviceHelpers.GetChannelModel(itemChangedType, channelDeviceTreeItem);

        op.Component = BootstrapDynamicComponent.CreateComponent<ChannelEditComponent>(new Dictionary<string, object?>
        {
             {nameof(ChannelEditComponent.OnValidSubmit), async () =>
            {
                await Task.Run(() =>GlobalData.ChannelRuntimeService.SaveChannelAsync(oneModel,itemChangedType,AutoRestartThread));
               await Notify();
            }},
            {nameof(ChannelEditComponent.Model),oneModel },
            {nameof(ChannelEditComponent.ValidateEnable),true },
            {nameof(ChannelEditComponent.BatchEditEnable),false },
            {nameof(ChannelEditComponent.PluginType),  pluginTypeEnum },
        });

        await DialogService.Show(op);

    }

    async Task CopyChannel(ContextMenuItem item, object value)
    {

        Channel oneModel = null;
        Dictionary<Device, List<Variable>> deviceDict = new();
        if (value is not ChannelDeviceTreeItem channelDeviceTreeItem) return;

        if (channelDeviceTreeItem.TryGetChannelRuntime(out var channelRuntime))
        {
            oneModel = channelRuntime.Adapt<Channel>();
            oneModel.Id = 0;

            deviceDict = channelRuntime.ReadDeviceRuntimes.ToDictionary(a => a.Value.Adapt<Device>(), a => a.Value.ReadOnlyVariableRuntimes.Select(a => a.Value).Adapt<List<Variable>>());
        }
        else
        {
            return;
        }


        var op = new DialogOption()
        {
            IsScrolling = false,
            ShowMaximizeButton = true,
            Size = Size.ExtraLarge,
            Title = item.Text,
            ShowFooter = false,
            ShowCloseButton = false,
        };

        op.Component = BootstrapDynamicComponent.CreateComponent<ChannelCopyComponent>(new Dictionary<string, object?>
        {
             {nameof(ChannelCopyComponent.OnSave), async (List<Channel> channels,Dictionary<Device,List<Variable>> devices) =>
            {

                await Task.Run(() =>GlobalData.ChannelRuntimeService.CopyAsync(channels,devices,AutoRestartThread, default));
                    await Notify();

            }},
            {nameof(ChannelCopyComponent.Model),oneModel },
            {nameof(ChannelCopyComponent.Devices),deviceDict },
        });

        await DialogService.Show(op);


    }


    async Task BatchEditChannel(ContextMenuItem item, object value)
    {



        Channel oldModel = null;
        Channel oneModel = null;
        IEnumerable<Channel>? changedModels = null;
        IEnumerable<Channel>? models = null;

        if (value is not ChannelDeviceTreeItem channelDeviceTreeItem) return;

        if (channelDeviceTreeItem.TryGetChannelRuntime(out var channelRuntime))
        {
            await EditChannel(item, value, ItemChangedType.Update);
            return;
        }
        //批量编辑只有分类和插件名称节点
        else if (channelDeviceTreeItem.TryGetPluginName(out var pluginName))
        {
            //插件名称
            var data = await GlobalData.GetCurrentUserChannels().ConfigureAwait(false);
            models = data.Where(a => a.PluginName == pluginName);
            oldModel = models.FirstOrDefault();
            changedModels = models;
            oneModel = oldModel.Adapt<Channel>();

        }
        else if (channelDeviceTreeItem.TryGetPluginType(out var pluginType))
        {
            //采集

            var data = await GlobalData.GetCurrentUserChannels().ConfigureAwait(false);
            models = data.Where(a => a.PluginType == pluginType);
            oldModel = models.FirstOrDefault();
            changedModels = models;
            oneModel = oldModel.Adapt<Channel>();


        }
        else
        {
            return;
        }
        var op = new DialogOption()
        {
            IsScrolling = false,
            ShowMaximizeButton = true,
            Size = Size.ExtraLarge,
            Title = item.Text,
            ShowFooter = false,
            ShowCloseButton = false,
        };

        op.Component = BootstrapDynamicComponent.CreateComponent<ChannelEditComponent>(new Dictionary<string, object?>
        {
             {nameof(ChannelEditComponent.OnValidSubmit), async () =>
            {
                Spinner.SetRun(true);
                await Task.Run(() => GlobalData.ChannelRuntimeService.BatchEditAsync(changedModels, oldModel, oneModel,AutoRestartThread));

              await Notify();
                await InvokeAsync(() =>
                {

                    Spinner.SetRun(false);
                });

            } },
            {nameof(ChannelEditComponent.Model),oneModel },
            {nameof(ChannelEditComponent.ValidateEnable),true },
            {nameof(ChannelEditComponent.BatchEditEnable),true },
        });

        await DialogService.Show(op);


    }


    async Task ExcelChannel(ContextMenuItem item, object value)
    {

        var op = new DialogOption()
        {
            IsScrolling = false,
            ShowMaximizeButton = true,
            Size = Size.ExtraExtraLarge,
            Title = item.Text,
            ShowFooter = false,
            ShowCloseButton = false,
        };

        var models = await GlobalData.GetCurrentUserChannels().ConfigureAwait(false);
        var uSheetDatas = ChannelServiceHelpers.ExportChannel(models);

        op.Component = BootstrapDynamicComponent.CreateComponent<USheet>(new Dictionary<string, object?>
        {
             {nameof(USheet.OnSave), async (USheetDatas data) =>
            {
                try
    {
                Spinner.SetRun(true);

                await Task.Run(async ()=>
                {
              var importData=await  ChannelServiceHelpers.ImportAsync(data);
                await    GlobalData.ChannelRuntimeService.ImportChannelAsync(importData,AutoRestartThread);
                })
                    ;
    }
finally
                {
               await Notify();
            await InvokeAsync( ()=>
            {

            Spinner.SetRun(false);
                });
                }


            }},
            {nameof(USheet.Model),uSheetDatas },
        });

        await DialogService.Show(op);

    }


    async Task DeleteChannel(ContextMenuItem item, object value)
    {
        var op = new DialogOption();
        {
            op.Size = Size.ExtraSmall;
            op.ShowCloseButton = false;
            op.ShowMaximizeButton = false;
            op.ShowSaveButton = false;
            op.Title = string.Empty;
            op.BodyTemplate = new RenderFragment(builder =>
             {
                 builder.OpenElement(0, "div");
                 builder.AddAttribute(1, "class", "swal2-actions");
                 builder.OpenComponent<Button>(2);
                 builder.AddComponentParameter(3, nameof(Button.Icon), "fa-solid fa-xmark");
                 builder.AddComponentParameter(4, nameof(Button.OnClick),
 EventCallback.Factory.Create<MouseEventArgs>(this, async e =>
 {
     await op.CloseDialogAsync();
     await DeleteCurrentChannel(item, value);
 }));
                 builder.AddComponentParameter(5, nameof(Button.Text), GatewayLocalizer["DeleteCurrentChannel"].Value);

                 builder.CloseComponent();

                 builder.OpenComponent<Button>(12);
                 builder.AddAttribute(13, "class", "ms-3");
                 builder.AddComponentParameter(14, nameof(Button.Icon), "fa-solid fa-xmark");
                 builder.AddComponentParameter(15, nameof(Button.OnClick), EventCallback.Factory.Create<MouseEventArgs>(this, async e =>
                 {
                     await op.CloseDialogAsync();
                     await DeleteAllChannel(item, value);
                 }));
                 builder.AddComponentParameter(16, nameof(Button.Text), GatewayLocalizer["DeleteAllChannel"].Value);

                 builder.CloseComponent();

                 builder.CloseElement();
             });

        }
        ;
        await DialogService.Show(op);
    }
    async Task DeleteCurrentChannel(ContextMenuItem item, object value)
    {
        IEnumerable<ChannelRuntime> modelIds = null;
        if (value is not ChannelDeviceTreeItem channelDeviceTreeItem) return;

        if (channelDeviceTreeItem.TryGetChannelRuntime(out var channelRuntime))
        {
            modelIds = new List<ChannelRuntime> { channelRuntime };
        }
        else if (channelDeviceTreeItem.TryGetPluginName(out var pluginName))
        {
            //插件名称
            var data = await GlobalData.GetCurrentUserChannels().ConfigureAwait(false);
            modelIds = data.Where(a => a.PluginName == pluginName);

        }
        else if (channelDeviceTreeItem.TryGetPluginType(out var pluginType))
        {
            //采集

            var data = await GlobalData.GetCurrentUserChannels().ConfigureAwait(false);
            modelIds = data.Where(a => a.PluginType == pluginType);

        }
        else
        {
            return;

        }

        try
        {
            var op = new SwalOption()
            {
                Title = GatewayLocalizer["DeleteConfirmTitle"],
                BodyTemplate = (__builder) =>
                {
                    var data = modelIds.Select(a => a.Name).ToJsonNetString();
                    __builder.OpenElement(0, "div");
                    __builder.AddAttribute(1, "class", "w-100 ");
                    __builder.OpenElement(2, "span");
                    __builder.AddAttribute(3, "class", "text-truncate px-2");
                    __builder.AddAttribute(4, "style", "display: flow;");
                    __builder.AddAttribute(5, "title", data);
                    __builder.AddContent(6, data);
                    __builder.CloseElement();
                    __builder.CloseElement();
                }
            };
            var ret = await SwalService.ShowModal(op);
            if (ret)
            {

                Spinner.SetRun(true);

                await Task.Run(() => GlobalData.ChannelRuntimeService.DeleteChannelAsync(modelIds.Select(a => a.Id), AutoRestartThread, default));
                await Notify();
                await InvokeAsync(() =>
                {
                    Spinner.SetRun(false);
                });
            }

        }
        catch (Exception ex)
        {
            await InvokeAsync(async () =>
            {
                await ToastService.Warn(ex);
            });
        }

    }
    async Task DeleteAllChannel(ContextMenuItem item, object value)
    {
        try
        {
            var op = new SwalOption()
            {
                Title = GatewayLocalizer["DeleteConfirmTitle"],
                BodyTemplate = (__builder) =>
                {
                    __builder.OpenElement(0, "div");
                    __builder.AddAttribute(1, "class", "w-100 ");
                    __builder.OpenElement(2, "span");
                    __builder.AddAttribute(3, "class", "text-truncate px-2");
                    __builder.AddAttribute(4, "style", "display: flow;");
                    __builder.AddAttribute(5, "title", GatewayLocalizer["AllChannel"]);
                    __builder.AddContent(6, GatewayLocalizer["AllChannel"]);
                    __builder.CloseElement();
                    __builder.CloseElement();
                }
            };
            var ret = await SwalService.ShowModal(op);
            if (ret)
            {
                Spinner.SetRun(true);

                var key = await GlobalData.GetCurrentUserChannels().ConfigureAwait(false);
                await Task.Run(() => GlobalData.ChannelRuntimeService.DeleteChannelAsync(key.Select(a => a.Id), AutoRestartThread, default));
                await Notify();
                await InvokeAsync(() =>
                {
                    Spinner.SetRun(false);
                });
            }

        }
        catch (Exception ex)
        {
            await InvokeAsync(async () =>
            {
                await ToastService.Warn(ex);
            });
        }

    }

    async Task ExportChannel(ContextMenuItem item, object value)
    {
        var op = new DialogOption();
        {
            op.Size = Size.ExtraSmall;
            op.ShowCloseButton = false;
            op.ShowMaximizeButton = false;
            op.ShowSaveButton = false;
            op.Title = string.Empty;
            op.BodyTemplate = new RenderFragment(builder =>
            {
                builder.OpenElement(0, "div");
                builder.AddAttribute(1, "class", "swal2-actions");
                builder.OpenComponent<Button>(2);
                builder.AddComponentParameter(3, nameof(Button.Icon), "fa-solid fa-xmark");
                builder.AddComponentParameter(4, nameof(Button.OnClick),
EventCallback.Factory.Create<MouseEventArgs>(this, async e =>
{
    await op.CloseDialogAsync();
    await ExportCurrentChannel(item, value);
}));
                builder.AddComponentParameter(5, nameof(Button.Text), GatewayLocalizer["ExportCurrentChannel"].Value);

                builder.CloseComponent();

                builder.OpenComponent<Button>(12);
                builder.AddAttribute(13, "class", "ms-3");
                builder.AddComponentParameter(14, nameof(Button.Icon), "fa-solid fa-xmark");
                builder.AddComponentParameter(15, nameof(Button.OnClick), EventCallback.Factory.Create<MouseEventArgs>(this, async e =>
                {
                    await op.CloseDialogAsync();
                    await ExportAllChannel(item, value);
                }));
                builder.AddComponentParameter(16, nameof(Button.Text), GatewayLocalizer["ExportAllChannel"].Value);

                builder.CloseComponent();

                builder.CloseElement();
            });

        }
    ;
        await DialogService.Show(op);
    }
    async Task ExportCurrentChannel(ContextMenuItem item, object value)
    {
        if (value is not ChannelDeviceTreeItem channelDeviceTreeItem) return;

        if (channelDeviceTreeItem.TryGetChannelRuntime(out var channelRuntime))
        {
            await GatewayExportService.OnChannelExport(new() { QueryPageOptions = new(), DeviceId = channelRuntime.Id });
        }
        else if (channelDeviceTreeItem.TryGetPluginName(out var pluginName))
        {
            //插件名称
            await GatewayExportService.OnChannelExport(new() { QueryPageOptions = new(), PluginName = pluginName });
        }
        else if (channelDeviceTreeItem.TryGetPluginType(out var pluginType))
        {
            await GatewayExportService.OnChannelExport(new ExportFilter() { QueryPageOptions = new(), PluginType = pluginType });
        }
        else
        {
            return;
        }

        // 返回 true 时自动弹出提示框
        await ToastService.Default();
    }
    async Task ExportAllChannel(ContextMenuItem item, object value)
    {
        await GatewayExportService.OnChannelExport(new() { QueryPageOptions = new() });

        // 返回 true 时自动弹出提示框
        await ToastService.Default();
    }


    async Task ImportChannel(ContextMenuItem item, object value)
    {
        var op = new DialogOption()
        {
            IsScrolling = true,
            ShowMaximizeButton = true,
            Size = Size.ExtraLarge,
            Title = item.Text,
            ShowFooter = false,
            ShowCloseButton = false,
            OnCloseAsync = async () =>
            {
                await InvokeAsync(StateHasChanged);
                //await InvokeAsync(table.QueryAsync);
            },
        };

        Func<IBrowserFile, Task<Dictionary<string, ImportPreviewOutputBase>>> preview = (a => GlobalData.ChannelRuntimeService.PreviewAsync(a));
        Func<Dictionary<string, ImportPreviewOutputBase>, Task> import = (async value =>
        {
            await InvokeAsync(() =>
            {
                Spinner.SetRun(true);

            });
            await Task.Run(() => GlobalData.ChannelRuntimeService.ImportChannelAsync(value, AutoRestartThread));
            await Notify();
            await InvokeAsync(() =>
            {
                Spinner.SetRun(false);
            });

        });
        op.Component = BootstrapDynamicComponent.CreateComponent<ImportExcel>(new Dictionary<string, object?>
        {
             {nameof(ImportExcel.Import),import },
            {nameof(ImportExcel.Preview),preview },
        });
        await DialogService.Show(op);

        //await InvokeAsync(table.QueryAsync);
    }


    #endregion

    #region 设备

    async Task CopyDevice(ContextMenuItem item, object value)
    {
        var op = new DialogOption()
        {
            IsScrolling = true,
            ShowMaximizeButton = true,
            Size = Size.ExtraLarge,
            Title = item.Text,
            ShowFooter = false,
            ShowCloseButton = false,
        };
        Device oneModel = null;
        List<Variable> variables = new();
        if (value is not ChannelDeviceTreeItem channelDeviceTreeItem) return;

        if (channelDeviceTreeItem.TryGetDeviceRuntime(out var deviceRuntime))
        {
            oneModel = deviceRuntime.Adapt<Device>();
            oneModel.Id = 0;

            variables = deviceRuntime.ReadOnlyVariableRuntimes.Select(a => a.Value).Adapt<List<Variable>>();
        }
        else
        {
            return;
        }

        op.Component = BootstrapDynamicComponent.CreateComponent<DeviceCopyComponent>(new Dictionary<string, object?>
        {
             {nameof(DeviceCopyComponent.OnSave), async (Dictionary<Device,List<Variable>> devices) =>
            {

                await Task.Run(() =>GlobalData.DeviceRuntimeService.CopyAsync(devices,AutoRestartThread, default));
               await Notify();

            }},
            {nameof(DeviceCopyComponent.Model),oneModel },
            {nameof(DeviceCopyComponent.Variables),variables },
        });

        await DialogService.Show(op);

    }


    async Task EditDevice(ContextMenuItem item, object value, ItemChangedType itemChangedType)
    {
        var op = new DialogOption()
        {
            IsScrolling = true,
            ShowMaximizeButton = true,
            Size = Size.ExtraLarge,
            Title = item.Text,
            ShowFooter = false,
            ShowCloseButton = false,
        };
        Device oneModel = null;
        if (value is not ChannelDeviceTreeItem channelDeviceTreeItem) return;

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
            oneModel.ChannelId = channelRuntime.Id;
        }
        else
        {
            return;
        }

        op.Component = BootstrapDynamicComponent.CreateComponent<DeviceEditComponent>(new Dictionary<string, object?>
        {
             {nameof(DeviceEditComponent.OnValidSubmit), async () =>
             {
                 await Task.Run(() =>GlobalData.DeviceRuntimeService.SaveDeviceAsync(oneModel,itemChangedType, AutoRestartThread));
               await Notify();
            }},
            {nameof(DeviceEditComponent.Model),oneModel },
            {nameof(DeviceEditComponent.AutoRestartThread),AutoRestartThread },
            {nameof(DeviceEditComponent.ValidateEnable),true },
            {nameof(DeviceEditComponent.BatchEditEnable),false },
        });

        await DialogService.Show(op);

    }

    async Task BatchEditDevice(ContextMenuItem item, object value)
    {

        var op = new DialogOption()
        {
            IsScrolling = true,
            ShowMaximizeButton = true,
            Size = Size.ExtraLarge,
            Title = item.Text,
            ShowFooter = false,
            ShowCloseButton = false,
        };

        Device oldModel = null;
        Device oneModel = null;
        IEnumerable<Device>? changedModels = null;
        IEnumerable<Device>? models = null;

        if (value is not ChannelDeviceTreeItem channelDeviceTreeItem) return;

        if (channelDeviceTreeItem.TryGetDeviceRuntime(out var deviceRuntime))
        {
            await EditDevice(item, value, ItemChangedType.Update);
            return;
        }
        else if (channelDeviceTreeItem.TryGetChannelRuntime(out var channelRuntime))
        {
            //插件名称
            var data = await GlobalData.GetCurrentUserDevices().ConfigureAwait(false);
            models = data.Where(a => a.ChannelId == channelRuntime.Id);
            oldModel = models.FirstOrDefault();
            changedModels = models;
            oneModel = oldModel.Adapt<Device>();
        }
        //批量编辑只有分类和插件名称节点
        else if (channelDeviceTreeItem.TryGetPluginName(out var pluginName))
        {
            //插件名称
            var data = await GlobalData.GetCurrentUserDevices().ConfigureAwait(false);
            models = data.Where(a => a.PluginName == pluginName); ;
            oldModel = models.FirstOrDefault();
            changedModels = models;
            oneModel = oldModel.Adapt<Device>();

        }
        else if (channelDeviceTreeItem.TryGetPluginType(out var pluginType))
        {
            //采集

            var data = await GlobalData.GetCurrentUserDevices().ConfigureAwait(false);
            models = data.Where(a => a.PluginType == pluginType); ;
            oldModel = models.FirstOrDefault();
            changedModels = models;
            oneModel = oldModel.Adapt<Device>();


        }
        else
        {
            return;
        }


        op.Component = BootstrapDynamicComponent.CreateComponent<DeviceEditComponent>(new Dictionary<string, object?>
        {
             {nameof(DeviceEditComponent.OnValidSubmit), async () =>
            {
                    await InvokeAsync( () =>
            {
                Spinner.SetRun(true);

                });
                await Task.Run(() =>GlobalData.DeviceRuntimeService.BatchEditAsync(changedModels,oldModel,oneModel,AutoRestartThread));
                await Notify();
                         await InvokeAsync(() =>
            {
                                Spinner.SetRun(false);
                });
            }},
            {nameof(DeviceEditComponent.Model),oneModel },
            {nameof(DeviceEditComponent.AutoRestartThread),AutoRestartThread },
            {nameof(DeviceEditComponent.ValidateEnable),true },
            {nameof(DeviceEditComponent.BatchEditEnable),true },
        });

        await DialogService.Show(op);

    }

    async Task ExcelDevice(ContextMenuItem item, object value)
    {

        var op = new DialogOption()
        {
            IsScrolling = false,
            ShowMaximizeButton = true,
            Size = Size.ExtraExtraLarge,
            Title = item.Text,
            ShowFooter = false,
            ShowCloseButton = false,
        };

        var models = await GlobalData.GetCurrentUserDevices().ConfigureAwait(false);
        var uSheetDatas = await DeviceServiceHelpers.ExportDeviceAsync(models);

        op.Component = BootstrapDynamicComponent.CreateComponent<USheet>(new Dictionary<string, object?>
        {
             {nameof(USheet.OnSave), async (USheetDatas data) =>
            {
                try
    {
                Spinner.SetRun(true);

                await Task.Run(async ()=>
                {
              var importData=await  DeviceServiceHelpers.ImportAsync(data);
                await    GlobalData.DeviceRuntimeService.ImportDeviceAsync(importData,AutoRestartThread);
                })
                    ;
    }
finally
                {
                await Notify();
                                 await InvokeAsync( ()=>
            {

            Spinner.SetRun(false);
                });
                }


            }},
            {nameof(USheet.Model),uSheetDatas },
        });

        await DialogService.Show(op);

    }


    async Task DeleteDevice(ContextMenuItem item, object value)
    {
        var op = new DialogOption();
        {
            op.Size = Size.ExtraSmall;
            op.ShowCloseButton = false;
            op.ShowMaximizeButton = false;
            op.ShowSaveButton = false;
            op.Title = string.Empty;
            op.BodyTemplate = new RenderFragment(builder =>
            {
                builder.OpenElement(0, "div");
                builder.AddAttribute(1, "class", "swal2-actions");
                builder.OpenComponent<Button>(2);
                builder.AddComponentParameter(3, nameof(Button.Icon), "fa-solid fa-xmark");
                builder.AddComponentParameter(4, nameof(Button.OnClick),
EventCallback.Factory.Create<MouseEventArgs>(this, async e =>
{
    await op.CloseDialogAsync();
    await DeleteCurrentDevice(item, value);
}));
                builder.AddComponentParameter(5, nameof(Button.Text), GatewayLocalizer["DeleteCurrentDevice"].Value);

                builder.CloseComponent();

                builder.OpenComponent<Button>(12);
                builder.AddAttribute(13, "class", "ms-3");
                builder.AddComponentParameter(14, nameof(Button.Icon), "fa-solid fa-xmark");
                builder.AddComponentParameter(15, nameof(Button.OnClick), EventCallback.Factory.Create<MouseEventArgs>(this, async e =>
                {
                    await op.CloseDialogAsync();
                    await DeleteAllDevice(item, value);
                }));
                builder.AddComponentParameter(16, nameof(Button.Text), GatewayLocalizer["DeleteAllDevice"].Value);

                builder.CloseComponent();

                builder.CloseElement();
            });

        }
        ;
        await DialogService.Show(op);
    }


    async Task DeleteCurrentDevice(ContextMenuItem item, object value)
    {
        IEnumerable<DeviceRuntime> modelIds = null;
        if (value is not ChannelDeviceTreeItem channelDeviceTreeItem) return;

        if (channelDeviceTreeItem.TryGetDeviceRuntime(out var deviceRuntime))
        {
            modelIds = new List<DeviceRuntime> { deviceRuntime };
        }
        else if (channelDeviceTreeItem.TryGetChannelRuntime(out var channelRuntime))
        {
            var data = await GlobalData.GetCurrentUserDevices().ConfigureAwait(false);
            modelIds = data.Where(a => a.ChannelId == channelRuntime.Id);
        }
        else if (channelDeviceTreeItem.TryGetPluginName(out var pluginName))
        {
            //插件名称
            var data = await GlobalData.GetCurrentUserDevices().ConfigureAwait(false);
            modelIds = data.Where(a => a.PluginName == pluginName);

        }
        else if (channelDeviceTreeItem.TryGetPluginType(out var pluginType))
        {
            //采集
            var data = await GlobalData.GetCurrentUserDevices().ConfigureAwait(false);
            modelIds = data.Where(a => a.PluginType == pluginType);
        }
        else
        {
            return;

        }

        try
        {
            var op = new SwalOption()
            {
                Title = GatewayLocalizer["DeleteConfirmTitle"],
                BodyTemplate = (__builder) =>
                {
                    var data = modelIds.Select(a => a.Name).ToJsonNetString();
                    __builder.OpenElement(0, "div");
                    __builder.AddAttribute(1, "class", "w-100 ");
                    __builder.OpenElement(2, "span");
                    __builder.AddAttribute(3, "class", "text-truncate px-2");
                    __builder.AddAttribute(4, "style", "display: flow;");
                    __builder.AddAttribute(5, "title", data);
                    __builder.AddContent(6, data);
                    __builder.CloseElement();
                    __builder.CloseElement();
                }
            };
            var ret = await SwalService.ShowModal(op);
            if (ret)
            {

                Spinner.SetRun(true);

                await Task.Run(() => GlobalData.DeviceRuntimeService.DeleteDeviceAsync(modelIds.Select(a => a.Id), AutoRestartThread, default));
                await Notify();
                await InvokeAsync(() =>
                {
                    Spinner.SetRun(false);
                });
            }

        }
        catch (Exception ex)
        {
            await InvokeAsync(async () =>
            {
                await ToastService.Warn(ex);
            });
        }

    }

    async Task DeleteAllDevice(ContextMenuItem item, object value)
    {
        try
        {
            var op = new SwalOption()
            {
                Title = GatewayLocalizer["DeleteConfirmTitle"],
                BodyTemplate = (__builder) =>
                {
                    __builder.OpenElement(0, "div");
                    __builder.AddAttribute(1, "class", "w-100 ");
                    __builder.OpenElement(2, "span");
                    __builder.AddAttribute(3, "class", "text-truncate px-2");
                    __builder.AddAttribute(4, "style", "display: flow;");
                    __builder.AddAttribute(5, "title", GatewayLocalizer["AllDevice"]);
                    __builder.AddContent(6, GatewayLocalizer["AllDevice"]);
                    __builder.CloseElement();
                    __builder.CloseElement();
                }
            };
            var ret = await SwalService.ShowModal(op);
            if (ret)
            {

                Spinner.SetRun(true);

                var data = await GlobalData.GetCurrentUserDevices().ConfigureAwait(false);

                await Task.Run(() => GlobalData.DeviceRuntimeService.DeleteDeviceAsync(data.Select(a => a.Id), AutoRestartThread, default));
                await Notify();
                await InvokeAsync(() =>
                {
                    Spinner.SetRun(false);
                });
            }

        }
        catch (Exception ex)
        {
            await InvokeAsync(async () =>
            {
                await ToastService.Warn(ex);
            });
        }

    }


    async Task ExportDevice(ContextMenuItem item, object value)
    {
        var op = new DialogOption();
        {
            op.Size = Size.ExtraSmall;
            op.ShowCloseButton = false;
            op.ShowMaximizeButton = false;
            op.ShowSaveButton = false;
            op.Title = string.Empty;
            op.BodyTemplate = new RenderFragment(builder =>
            {
                builder.OpenElement(0, "div");
                builder.AddAttribute(1, "class", "swal2-actions");
                builder.OpenComponent<Button>(2);
                builder.AddComponentParameter(3, nameof(Button.Icon), "fa-solid fa-xmark");
                builder.AddComponentParameter(4, nameof(Button.OnClick),
EventCallback.Factory.Create<MouseEventArgs>(this, async e =>
{
    await op.CloseDialogAsync();
    await ExportCurrentDevice(item, value);
}));
                builder.AddComponentParameter(5, nameof(Button.Text), GatewayLocalizer["ExportCurrentDevice"].Value);

                builder.CloseComponent();

                builder.OpenComponent<Button>(12);
                builder.AddAttribute(13, "class", "ms-3");
                builder.AddComponentParameter(14, nameof(Button.Icon), "fa-solid fa-xmark");
                builder.AddComponentParameter(15, nameof(Button.OnClick), EventCallback.Factory.Create<MouseEventArgs>(this, async e =>
                {
                    await op.CloseDialogAsync();
                    await ExportAllDevice(item, value);
                }));
                builder.AddComponentParameter(16, nameof(Button.Text), GatewayLocalizer["ExportAllDevice"].Value);

                builder.CloseComponent();

                builder.CloseElement();
            });

        }
        ;
        await DialogService.Show(op);
    }


    async Task ExportCurrentDevice(ContextMenuItem item, object value)
    {
        if (value is not ChannelDeviceTreeItem channelDeviceTreeItem) return;

        if (channelDeviceTreeItem.TryGetDeviceRuntime(out var deviceRuntime))
        {
            await GatewayExportService.OnDeviceExport(new() { QueryPageOptions = new(), DeviceId = deviceRuntime.Id });
        }
        else if (channelDeviceTreeItem.TryGetChannelRuntime(out var channelRuntime))
        {
            await GatewayExportService.OnDeviceExport(new() { QueryPageOptions = new(), ChannelId = channelRuntime.Id });
        }
        else if (channelDeviceTreeItem.TryGetPluginName(out var pluginName))
        {
            //插件名称
            await GatewayExportService.OnDeviceExport(new() { QueryPageOptions = new(), PluginName = pluginName });
        }
        else if (channelDeviceTreeItem.TryGetPluginType(out var pluginType))
        {
            //采集
            await GatewayExportService.OnDeviceExport(new() { QueryPageOptions = new(), PluginType = pluginType });
        }
        else
        {
            return;
        }

        // 返回 true 时自动弹出提示框
        await ToastService.Default();
    }
    async Task ExportAllDevice(ContextMenuItem item, object value)
    {
        await GatewayExportService.OnDeviceExport(new() { QueryPageOptions = new() });

        // 返回 true 时自动弹出提示框
        await ToastService.Default();
    }

    async Task ImportDevice(ContextMenuItem item, object value)
    {
        var op = new DialogOption()
        {
            IsScrolling = true,
            ShowMaximizeButton = true,
            Size = Size.ExtraLarge,
            Title = item.Text,
            ShowFooter = false,
            ShowCloseButton = false,
            OnCloseAsync = async () =>
            {
                await InvokeAsync(StateHasChanged);
            },
        };

        Func<IBrowserFile, Task<Dictionary<string, ImportPreviewOutputBase>>> preview = (a => GlobalData.DeviceRuntimeService.PreviewAsync(a));
        Func<Dictionary<string, ImportPreviewOutputBase>, Task> import = (async value =>
        {
            await InvokeAsync(() =>
            {

                Spinner.SetRun(true);

            });

            await Task.Run(() => GlobalData.DeviceRuntimeService.ImportDeviceAsync(value, AutoRestartThread));
            await Notify();
            await InvokeAsync(() =>
            {
                Spinner.SetRun(false);
            });

        });
        op.Component = BootstrapDynamicComponent.CreateComponent<ImportExcel>(new Dictionary<string, object?>
        {
             {nameof(ImportExcel.Import),import },
            {nameof(ImportExcel.Preview),preview },
        });
        await DialogService.Show(op);

        //await InvokeAsync(table.QueryAsync);
    }


    #endregion



    [Inject]
    SwalService SwalService { get; set; }
    [Inject]
    ToastService ToastService { get; set; }

    [Parameter]
    [NotNull]
    public ChannelDeviceTreeItem Value { get; set; }

    [Parameter]
    public Func<ChannelDeviceTreeItem, Task> ChannelDeviceChanged { get; set; }

    [NotNull]
    private List<TreeViewItem<ChannelDeviceTreeItem>> Items { get; set; }

    [Inject]
    private IStringLocalizer<ThingsGateway.Razor._Imports> RazorLocalizer { get; set; }

    [Inject]
    private IStringLocalizer<ThingsGateway.Gateway.Razor.ChannelDeviceTree> Localizer { get; set; }

    [Inject]
    private IStringLocalizer<ThingsGateway.Gateway.Razor._Imports> GatewayLocalizer { get; set; }

    [Inject]
    private IStringLocalizer<ThingsGateway.Admin.Razor._Imports> AdminLocalizer { get; set; }

    private async Task OnTreeItemClick(TreeViewItem<ChannelDeviceTreeItem> item)
    {
        if (Value != item.Value)
        {
            Value = item.Value;
            if (ChannelDeviceChanged != null)
            {
                await ChannelDeviceChanged.Invoke(item.Value);
            }
        }
        else
        {
            Value = item.Value;
        }

    }

    private List<TreeViewItem<ChannelDeviceTreeItem>> ZItem;


    private ChannelDeviceTreeItem CollectItem = new() { ChannelDevicePluginType = ChannelDevicePluginTypeEnum.PluginType, PluginType = PluginTypeEnum.Collect };
    private ChannelDeviceTreeItem BusinessItem = new() { ChannelDevicePluginType = ChannelDevicePluginTypeEnum.PluginType, PluginType = PluginTypeEnum.Business };
    private ChannelDeviceTreeItem UnknownItem = new() { ChannelDevicePluginType = ChannelDevicePluginTypeEnum.PluginType, PluginType = null };

    private TreeViewItem<ChannelDeviceTreeItem> UnknownTreeViewItem;
    protected override async Task OnInitializedAsync()
    {


        UnknownTreeViewItem = new TreeViewItem<ChannelDeviceTreeItem>(UnknownItem)
        {
            Text = GatewayLocalizer["Unknown"],
            IsActive = Value == UnknownItem,
            IsExpand = true,
        };
        ZItem = new List<TreeViewItem<ChannelDeviceTreeItem>>() {new TreeViewItem<ChannelDeviceTreeItem>(CollectItem)
        {
            Text = GatewayLocalizer["Collect"],
            IsActive = Value == CollectItem,
            IsExpand = true,
        },
        new TreeViewItem<ChannelDeviceTreeItem>(BusinessItem)
        {
            Text = GatewayLocalizer["Business"],
            IsActive = Value == BusinessItem,
            IsExpand = true,
        }};

        var channels = await GlobalData.GetCurrentUserChannels().ConfigureAwait(false);

        ZItem[0].Items = ResourceUtil.BuildTreeItemList(channels.Where(a => a.IsCollect == true), new List<ChannelDeviceTreeItem> { Value }, RenderTreeItem);
        ZItem[1].Items = ResourceUtil.BuildTreeItemList(channels.Where(a => a.IsCollect == false), new List<ChannelDeviceTreeItem> { Value }, RenderTreeItem);
        var item2 = ResourceUtil.BuildTreeItemList(channels.Where(a => a.IsCollect == null), new List<ChannelDeviceTreeItem> { Value }, RenderTreeItem);
        if (item2.Count > 0)
        {
            UnknownTreeViewItem.Items = item2;
            if (ZItem.Count >= 3)
            {

            }
            else
            {
                ZItem.Add(UnknownTreeViewItem);
            }

        }
        else
        {
            if (ZItem.Count >= 3)
            {
                ZItem.Remove(UnknownTreeViewItem);
            }
            else
            {
            }
        }

        Items = ZItem;
        ChannelRuntimeDispatchService.Subscribe(Refresh);
        DeviceRuntimeDispatchService.Subscribe(Refresh);


        _ = Task.Run(async () =>
        {
            while (!Disposed)
            {
                try
                {
                    await InvokeAsync(StateHasChanged);
                }
                catch
                {

                }
                finally
                {
                    await Task.Delay(5000);
                }
            }
        });

        await base.OnInitializedAsync();
    }

    private WaitLock WaitLock = new();

    private volatile bool _isExecuting = false;

    private async Task Notify()
    {
        if (_isExecuting) return;
        try
        {
            await WaitLock.WaitAsync();

            if (_isExecuting) return;

            _isExecuting = true;

            try
            {
                if (Disposed) return;
                await OnClickSearch(SearchText);
                if (ChannelDeviceChanged != null)
                {
                    await ChannelDeviceChanged.Invoke(Value);
                }
                await InvokeAsync(StateHasChanged);
            }
            finally
            {
                await Task.Delay(2000);
                _isExecuting = false;
            }
        }
        finally
        {
            WaitLock.Release();
        }
    }
    private async Task Refresh(DispatchEntry<DeviceRuntime> entry)
    {
        await Notify();
    }
    private async Task Refresh(DispatchEntry<ChannelRuntime> entry)
    {
        await Notify();
    }
    [Inject]
    private IDispatchService<DeviceRuntime> DeviceRuntimeDispatchService { get; set; }
    [Inject]
    private IDispatchService<ChannelRuntime> ChannelRuntimeDispatchService { get; set; }
    private string SearchText;

    private async Task<List<TreeViewItem<ChannelDeviceTreeItem>>> OnClickSearch(string searchText)
    {
        SearchText = searchText;

        var channels = await GlobalData.GetCurrentUserChannels().ConfigureAwait(false);
        if (searchText.IsNullOrWhiteSpace())
        {
            var items = channels.WhereIF(!searchText.IsNullOrEmpty(), a => a.Name.Contains(searchText));

            ZItem[0].Items = ResourceUtil.BuildTreeItemList(items.Where(a => a.IsCollect == true), new List<ChannelDeviceTreeItem> { Value }, RenderTreeItem, items: ZItem[0].Items);
            ZItem[1].Items = ResourceUtil.BuildTreeItemList(items.Where(a => a.IsCollect == false), new List<ChannelDeviceTreeItem> { Value }, RenderTreeItem, items: ZItem[1].Items);

            var item2 = ResourceUtil.BuildTreeItemList(items.Where(a => a.IsCollect == null), new List<ChannelDeviceTreeItem> { Value }, RenderTreeItem);
            if (item2.Count > 0)
            {
                UnknownTreeViewItem.Items = item2;
                if (ZItem.Count >= 3)
                {

                }
                else
                {
                    ZItem.Add(UnknownTreeViewItem);
                }

            }
            else
            {
                if (ZItem.Count >= 3)
                {
                    ZItem.Remove(UnknownTreeViewItem);
                }
                else
                {
                }
            }
            Items = ZItem;
            return Items;
        }
        else
        {
            var items = channels.WhereIF(!searchText.IsNullOrEmpty(), a => a.Name.Contains(searchText));
            var devices = await GlobalData.GetCurrentUserDevices().ConfigureAwait(false);
            var deviceItems = devices.WhereIF(!searchText.IsNullOrEmpty(), a => a.Name.Contains(searchText));

            Dictionary<ChannelRuntime, List<DeviceRuntime>> collectChannelDevices = new();
            Dictionary<ChannelRuntime, List<DeviceRuntime>> businessChannelDevices = new();
            Dictionary<ChannelRuntime, List<DeviceRuntime>> otherChannelDevices = new();

            foreach (var item in items)
            {
                if (item.PluginType == PluginTypeEnum.Collect)
                    collectChannelDevices.Add(item, new());
                else if (item.PluginType == PluginTypeEnum.Collect)
                    businessChannelDevices.Add(item, new());
                else
                    otherChannelDevices.Add(item, new());

            }
            foreach (var item in deviceItems.Where(a => a.IsCollect == true))
            {
                if (collectChannelDevices.TryGetValue(item.ChannelRuntime, out var list))
                {
                    list.Add(item);
                }
                else
                {
                    collectChannelDevices[item.ChannelRuntime] = new List<DeviceRuntime> { item };
                }
            }
            foreach (var item in deviceItems.Where(a => a.IsCollect == false))
            {
                if (businessChannelDevices.TryGetValue(item.ChannelRuntime, out var list))
                {
                    list.Add(item);
                }
                else
                {
                    businessChannelDevices[item.ChannelRuntime] = new List<DeviceRuntime> { item };
                }
            }
            foreach (var item in deviceItems.Where(a => a.IsCollect == null))
            {
                if (otherChannelDevices.TryGetValue(item.ChannelRuntime, out var list))
                {
                    list.Add(item);
                }
                else
                {
                    otherChannelDevices[item.ChannelRuntime] = new List<DeviceRuntime> { item };
                }
            }

            ZItem[0].Items = collectChannelDevices.BuildTreeItemList(new List<ChannelDeviceTreeItem> { Value }, RenderTreeItem, items: ZItem[0].Items);
            ZItem[1].Items = businessChannelDevices.BuildTreeItemList(new List<ChannelDeviceTreeItem> { Value }, RenderTreeItem, items: ZItem[1].Items);
            var item2 = otherChannelDevices.BuildTreeItemList(new List<ChannelDeviceTreeItem> { Value }, RenderTreeItem);
            if (item2.Count > 0)
            {
                UnknownTreeViewItem.Items = item2;
                if (ZItem.Count >= 3)
                {

                }
                else
                {
                    ZItem.Add(UnknownTreeViewItem);
                }

            }
            else
            {
                if (ZItem.Count >= 3)
                {
                    ZItem.Remove(UnknownTreeViewItem);
                }
                else
                {
                }
            }

            Items = ZItem;
            return Items;
        }

    }

    private static bool ModelEqualityComparer(ChannelDeviceTreeItem x, ChannelDeviceTreeItem y)
    {
        if (x.ChannelDevicePluginType == y.ChannelDevicePluginType)
        {
            if (x.ChannelDevicePluginType == ChannelDevicePluginTypeEnum.Device)
            {
                return x.DeviceRuntime.Id == y.DeviceRuntime.Id; ;
            }
            else if (x.ChannelDevicePluginType == ChannelDevicePluginTypeEnum.PluginType)
            {
                return x.PluginType == y.PluginType;

            }
            else if (x.ChannelDevicePluginType == ChannelDevicePluginTypeEnum.Channel)
            {
                return x.ChannelRuntime.Id == y.ChannelRuntime.Id;
            }
            else if (x.ChannelDevicePluginType == ChannelDevicePluginTypeEnum.PluginName)
            {
                return x.PluginName == y.PluginName;
            }
        }
        return false;
    }
    private bool Disposed;
    protected override ValueTask DisposeAsync(bool disposing)
    {
        Disposed = true;
        ChannelRuntimeDispatchService.UnSubscribe(Refresh);
        DeviceRuntimeDispatchService.UnSubscribe(Refresh);
        return base.DisposeAsync(disposing);
    }

    ChannelDeviceTreeItem? SelectModel = default;

    Task OnBeforeShowCallback(object? item)
    {
        if (item is ChannelDeviceTreeItem channelDeviceTreeItem)
        {
            SelectModel = channelDeviceTreeItem;
        }
        else
        {
            SelectModel = null;
        }
        return Task.CompletedTask;
    }
    protected override void OnAfterRender(bool firstRender)
    {
        base.OnAfterRender(firstRender);
    }

}
