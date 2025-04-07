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

using ThingsGateway.Admin.Application;
using ThingsGateway.Extension.Generic;
using ThingsGateway.Gateway.Application;
using ThingsGateway.NewLife.Extension;

namespace ThingsGateway.Gateway.Razor;

public partial class DeviceTable : IDisposable
{

    public bool Disposed { get; set; }

    [Parameter]
    public IEnumerable<DeviceRuntime>? Items { get; set; } = Enumerable.Empty<DeviceRuntime>();

    public void Dispose()
    {
        Disposed = true;
        GC.SuppressFinalize(this);
    }

    protected override void OnInitialized()
    {
        _ = RunTimerAsync();
        base.OnInitialized();
    }


    private async Task RunTimerAsync()
    {
        while (!Disposed)
        {
            try
            {
                if (table != null)
                    await table.QueryAsync();
            }
            catch (Exception ex)
            {
                NewLife.Log.XTrace.WriteException(ex);
            }
            finally
            {
                await Task.Delay(1000);
            }
        }
    }

    #region 查询

    private QueryPageOptions _option = new();
    private Task<QueryData<DeviceRuntime>> OnQueryAsync(QueryPageOptions options)
    {
        var data = Items
                .WhereIf(!options.SearchText.IsNullOrWhiteSpace(), a => a.Name.Contains(options.SearchText))
                .GetQueryData(options);
        _option = options;
        return Task.FromResult(data);
    }

    #endregion 查询

    #region 编辑

    #region 修改
    private async Task Copy(IEnumerable<DeviceRuntime> devices)
    {

        if (!devices.Any())
        {
            await ToastService.Warning(null, RazorLocalizer["PleaseSelect"]);
            return;
        }

        Device oneModel = null;
        List<Variable> variables = new();
        var deviceRuntime = devices.FirstOrDefault();
        oneModel = deviceRuntime.Adapt<Device>();
        oneModel.Id = 0;

        variables = deviceRuntime.ReadOnlyVariableRuntimes.Select(a => a.Value).Adapt<List<Variable>>();


        var op = new DialogOption()
        {
            IsScrolling = false,
            ShowMaximizeButton = true,
            Size = Size.ExtraLarge,
            Title = RazorLocalizer["Copy"],
            ShowFooter = false,
            ShowCloseButton = false,
        };

        op.Component = BootstrapDynamicComponent.CreateComponent<DeviceCopyComponent>(new Dictionary<string, object?>
        {
             {nameof(DeviceCopyComponent.OnSave), async (Dictionary<Device,List<Variable>> devices) =>
            {

                await Task.Run(() =>GlobalData.DeviceRuntimeService.CopyAsync(devices,AutoRestartThread, default));
                    await table.QueryAsync();

            }},
            {nameof(DeviceCopyComponent.Model),oneModel },
            {nameof(DeviceCopyComponent.Variables),variables },
        });

        await DialogService.Show(op);



    }

    private async Task BatchEdit(IEnumerable<Device> changedModels)
    {
        var oldModel = changedModels.FirstOrDefault();//默认值显示第一个
        if (oldModel == null)
        {
            await ToastService.Warning(null, RazorLocalizer["PleaseSelect"]);
            return;
        }

        var oneModel = oldModel.Adapt<Device>();//默认值显示第一个

        var op = new DialogOption()
        {
            IsScrolling = false,
            ShowMaximizeButton = true,
            Size = Size.ExtraLarge,
            Title = RazorLocalizer["BatchEdit"],
            ShowFooter = false,
            ShowCloseButton = false,
        };

        op.Component = BootstrapDynamicComponent.CreateComponent<DeviceEditComponent>(new Dictionary<string, object?>
        {
             {nameof(DeviceEditComponent.OnValidSubmit), async () =>
            {
                await Task.Run(() => GlobalData.DeviceRuntimeService.BatchEditAsync(changedModels, oldModel, oneModel,AutoRestartThread));

                   await InvokeAsync(table.QueryAsync);

            } },
            {nameof(DeviceEditComponent.Model),oneModel },
            {nameof(DeviceEditComponent.ValidateEnable),true },
            {nameof(DeviceEditComponent.BatchEditEnable),true },
        });

        await DialogService.Show(op);



    }


    private async Task<bool> Delete(IEnumerable<Device> devices)
    {
        try
        {
            return await Task.Run(async () =>
            {
                return await GlobalData.DeviceRuntimeService.DeleteDeviceAsync(devices.Select(a => a.Id), AutoRestartThread, default);
            });
        }
        catch (Exception ex)
        {
            await InvokeAsync(async () =>
            {
                await ToastService.Warn(ex);
            });
            return false;
        }
    }

    private async Task<bool> Save(Device device, ItemChangedType itemChangedType)
    {
        try
        {
            return await Task.Run(() => GlobalData.DeviceRuntimeService.SaveDeviceAsync(device, itemChangedType, AutoRestartThread));
        }
        catch (Exception ex)
        {
            await ToastService.Warn(ex);
            return false;
        }
    }

    #endregion 修改

    private Task<DeviceRuntime> OnAdd()
    {
        return Task.FromResult(ChannelDeviceHelpers.GetDeviceModel(ItemChangedType.Add, SelectModel).Adapt<DeviceRuntime>());
    }


    #region 导出

    [Inject]
    [NotNull]
    private IGatewayExportService? GatewayExportService { get; set; }

    private async Task ExcelExportAsync(ITableExportContext<DeviceRuntime> tableExportContext, bool all = false)
    {
        if (all)
        {
            await GatewayExportService.OnDeviceExport(new() { QueryPageOptions = new() });
        }
        else
        {
            switch (SelectModel.ChannelDevicePluginType)
            {

                case ChannelDevicePluginTypeEnum.PluginName:
                    await GatewayExportService.OnDeviceExport(new() { QueryPageOptions = new(), PluginName = SelectModel.PluginName });
                    break;
                case ChannelDevicePluginTypeEnum.Channel:
                    await GatewayExportService.OnDeviceExport(new() { QueryPageOptions = new(), ChannelId = SelectModel.ChannelRuntime.Id });
                    break;
                case ChannelDevicePluginTypeEnum.Device:
                    await GatewayExportService.OnDeviceExport(new() { QueryPageOptions = new(), DeviceId = SelectModel.DeviceRuntime.Id, PluginType = SelectModel.DeviceRuntime.PluginType });
                    break;
                default:
                    await GatewayExportService.OnDeviceExport(new() { QueryPageOptions = new() });

                    break;
            }

        }

        // 返回 true 时自动弹出提示框
        await ToastService.Default();
    }

    async Task ExcelDeviceAsync(ITableExportContext<DeviceRuntime> tableExportContext)
    {

        var op = new DialogOption()
        {
            IsScrolling = false,
            ShowMaximizeButton = true,
            Size = Size.ExtraExtraLarge,
            Title = GatewayLocalizer["ExcelDevice"],
            ShowFooter = false,
            ShowCloseButton = false,
        };

        var option = _option;
        option.IsPage = false;
        var models = Items
                .WhereIf(!option.SearchText.IsNullOrWhiteSpace(), a => a.Name.Contains(option.SearchText)).GetData(option, out var total).ToList();
        if (models.Count > 50000)
        {
            await ToastService.Warning("online Excel max data count 50000");
            return;
        }
        var uSheetDatas = await DeviceServiceHelpers.ExportDeviceAsync(models);

        op.Component = BootstrapDynamicComponent.CreateComponent<USheet>(new Dictionary<string, object?>
        {
             {nameof(USheet.OnSave), async (USheetDatas data) =>
            {
                try
    {
                await Task.Run(async ()=>
                {
              var importData=await  DeviceServiceHelpers.ImportAsync(data);
                await    GlobalData.DeviceRuntimeService.ImportDeviceAsync(importData,AutoRestartThread);
                })
                    ;
    }
finally
                {
                                 await InvokeAsync( async ()=>
            {

                    await table.QueryAsync();
             StateHasChanged();
                });
                }


            }},
            {nameof(USheet.Model),uSheetDatas },
        });

        await DialogService.Show(op);

    }



    private async Task ExcelImportAsync(ITableExportContext<DeviceRuntime> tableExportContext)
    {
        var op = new DialogOption()
        {
            IsScrolling = true,
            ShowMaximizeButton = true,
            Size = Size.ExtraLarge,
            Title = GatewayLocalizer["ImportDevice"],
            ShowFooter = false,
            ShowCloseButton = false,
            OnCloseAsync = async () =>
            {
                await InvokeAsync(table.QueryAsync);
            },
        };

        Func<IBrowserFile, Task<Dictionary<string, ImportPreviewOutputBase>>> preview = (a => GlobalData.DeviceRuntimeService.PreviewAsync(a));
        Func<Dictionary<string, ImportPreviewOutputBase>, Task> import = (value => GlobalData.DeviceRuntimeService.ImportDeviceAsync(value, AutoRestartThread));
        op.Component = BootstrapDynamicComponent.CreateComponent<ImportExcel>(new Dictionary<string, object?>
        {
             {nameof(ImportExcel.Import),import },
            {nameof(ImportExcel.Preview),preview },
        });
        await DialogService.Show(op);

    }

    #endregion 导出

    #region 清空

    private async Task ClearAsync()
    {
        try
        {
            await Task.Run(async () =>
            {

                await GlobalData.DeviceRuntimeService.DeleteDeviceAsync(Items.Select(a => a.Id), AutoRestartThread, default);
                await InvokeAsync(async () =>
                {
                    await ToastService.Default();
                    await InvokeAsync(table.QueryAsync);
                });
            });
        }
        catch (Exception ex)
        {
            await InvokeAsync(async () =>
            {
                await ToastService.Warn(ex);
            });
        }

    }
    #endregion

    [Parameter]
    public bool AutoRestartThread { get; set; }
    [Parameter]
    public ChannelDeviceTreeItem SelectModel { get; set; }
    [Inject]
    [NotNull]
    public IStringLocalizer<ThingsGateway.Gateway.Razor._Imports>? GatewayLocalizer { get; set; }
    #endregion

}
