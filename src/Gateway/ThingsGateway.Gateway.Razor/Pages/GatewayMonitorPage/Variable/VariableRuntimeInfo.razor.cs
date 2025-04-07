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
using Microsoft.Extensions.Options;

using ThingsGateway.Admin.Application;
using ThingsGateway.Extension.Generic;
using ThingsGateway.Gateway.Application;
using ThingsGateway.NewLife.Extension;
using ThingsGateway.NewLife.Json.Extension;

namespace ThingsGateway.Gateway.Razor;

public partial class VariableRuntimeInfo : IDisposable
{
    [Inject]
    private IOptions<WebsiteOptions>? WebsiteOption { get; set; }
    public bool Disposed { get; set; }

    [Parameter]
    public IEnumerable<VariableRuntime>? Items { get; set; } = Enumerable.Empty<VariableRuntime>();

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

    /// <summary>
    /// IntFormatter
    /// </summary>
    /// <param name="d"></param>
    /// <returns></returns>
    private static Task<string> JsonFormatter(object? d)
    {
        var ret = "";
        if (d is TableColumnContext<VariableRuntime, object?> data && data?.Value != null)
        {
            ret = data.Value.ToJsonNetString();
        }
        return Task.FromResult(ret);
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
    private Task<QueryData<VariableRuntime>> OnQueryAsync(QueryPageOptions options)
    {
        var data = Items
                .WhereIf(!options.SearchText.IsNullOrWhiteSpace(), a => a.Name.Contains(options.SearchText))
                .GetQueryData(options);
        _option = options;
        return Task.FromResult(data);
    }

    #endregion 查询

    #region 写入变量

    private string WriteValue { get; set; }

    private async Task OnWriteVariable(VariableRuntime variableRuntime)
    {
        try
        {
            var data = await Task.Run(async () => await variableRuntime.RpcAsync(WriteValue));
            if (!data.IsSuccess)
            {
                await ToastService.Warning(null, data.ErrorMessage);
            }
            else
            {
                await ToastService.Default();
            }
        }
        catch (Exception ex)
        {
            await ToastService.Warn(ex);
        }
    }

    #endregion 写入变量



    #region 编辑


    private int TestVariableCount { get; set; }
    private int TestDeviceCount { get; set; }

    private string SlaveUrl { get; set; }

    #region 修改
    private async Task Copy(IEnumerable<Variable> variables)
    {
        var op = new DialogOption()
        {
            IsScrolling = false,
            ShowMaximizeButton = true,
            Size = Size.ExtraLarge,
            Title = RazorLocalizer["Copy"],
            ShowFooter = false,
            ShowCloseButton = false,
        };
        if (!variables.Any())
        {
            await ToastService.Warning(null, RazorLocalizer["PleaseSelect"]);
            return;
        }
        op.Component = BootstrapDynamicComponent.CreateComponent<VariableCopyComponent>(new Dictionary<string, object?>
        {
             {nameof(VariableCopyComponent.OnSave), async (List<Variable> variables1) =>
            {

                await Task.Run(() =>GlobalData.VariableRuntimeService.AddBatchAsync(variables1,AutoRestartThread,default));
                await InvokeAsync(table.QueryAsync);

            }},
            {nameof(VariableCopyComponent.Model),variables },
        });

        await DialogService.Show(op);

    }

    private async Task BatchEdit(IEnumerable<Variable> variables)
    {
        var op = new DialogOption()
        {
            IsScrolling = false,
            ShowMaximizeButton = true,
            Size = Size.ExtraLarge,
            Title = RazorLocalizer["BatchEdit"],
            ShowFooter = false,
            ShowCloseButton = false,
        };
        var oldModel = variables.FirstOrDefault();//默认值显示第一个
        if (oldModel == null)
        {
            await ToastService.Warning(null, RazorLocalizer["PleaseSelect"]);
            return;
        }

        var model = oldModel.Adapt<Variable>();//默认值显示第一个
        op.Component = BootstrapDynamicComponent.CreateComponent<VariableEditComponent>(new Dictionary<string, object?>
        {
             {nameof(VariableEditComponent.OnValidSubmit), async () =>
            {
                await Task.Run(()=> GlobalData. VariableRuntimeService.BatchEditAsync(variables,oldModel,model, AutoRestartThread,default));

                await InvokeAsync(table.QueryAsync);
            }},
            {nameof(VariableEditComponent.AutoRestartThread),AutoRestartThread },
            {nameof(VariableEditComponent.Model),model },
            {nameof(VariableEditComponent.ValidateEnable),true },
            {nameof(VariableEditComponent.BatchEditEnable),true },
        });
        await DialogService.Show(op);
    }


    private async Task<bool> Delete(IEnumerable<Variable> variables)
    {
        try
        {
            return await Task.Run(async () =>
            {
                return await GlobalData.VariableRuntimeService.DeleteVariableAsync(variables.Select(a => a.Id), AutoRestartThread, default);
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

    private async Task<bool> Save(Variable variable, ItemChangedType itemChangedType)
    {
        try
        {
            variable.VariablePropertyModels ??= new();
            foreach (var item in variable.VariablePropertyModels)
            {
                var result = (!PluginServiceUtil.HasDynamicProperty(item.Value.Value)) || (item.Value.ValidateForm?.Validate() != false);
                if (result == false)
                {
                    return false;
                }
            }

            variable.VariablePropertys = PluginServiceUtil.SetDict(variable.VariablePropertyModels);
            return await Task.Run(() => GlobalData.VariableRuntimeService.SaveVariableAsync(variable, itemChangedType, AutoRestartThread, default));
        }
        catch (Exception ex)
        {
            await ToastService.Warn(ex);
            return false;
        }
    }

    #endregion 修改

    private Task<VariableRuntime> OnAdd()
    {
        return Task.FromResult(new VariableRuntime()
        {
            DeviceId = SelectModel?.DeviceRuntime?.IsCollect == true ? SelectModel?.DeviceRuntime?.Id ?? 0 : 0
        });
    }

    #region 导出

    [Inject]
    [NotNull]
    private IGatewayExportService? GatewayExportService { get; set; }

    private async Task ExcelExportAsync(ITableExportContext<VariableRuntime> tableExportContext, bool all = false)
    {
        if (all)
        {
            await GatewayExportService.OnVariableExport(new() { QueryPageOptions = new() });
        }
        else
        {
            switch (SelectModel.ChannelDevicePluginType)
            {

                case ChannelDevicePluginTypeEnum.PluginName:
                    await GatewayExportService.OnVariableExport(new() { QueryPageOptions = new(), PluginName = SelectModel.PluginName });
                    break;
                case ChannelDevicePluginTypeEnum.Channel:
                    await GatewayExportService.OnVariableExport(new() { QueryPageOptions = new(), ChannelId = SelectModel.ChannelRuntime.Id });
                    break;
                case ChannelDevicePluginTypeEnum.Device:
                    await GatewayExportService.OnVariableExport(new() { QueryPageOptions = new(), DeviceId = SelectModel.DeviceRuntime.Id, PluginType = SelectModel.DeviceRuntime.PluginType });
                    break;
                default:
                    await GatewayExportService.OnVariableExport(new() { QueryPageOptions = new() });

                    break;
            }

        }

        // 返回 true 时自动弹出提示框
        await ToastService.Default();
    }

    async Task ExcelVariableAsync(ITableExportContext<VariableRuntime> tableExportContext)
    {

        var op = new DialogOption()
        {
            IsScrolling = false,
            ShowMaximizeButton = true,
            Size = Size.ExtraExtraLarge,
            Title = GatewayLocalizer["ExcelVariable"],
            ShowFooter = false,
            ShowCloseButton = false,
        };
        var models = Items
                .WhereIf(!_option.SearchText.IsNullOrWhiteSpace(), a => a.Name.Contains(_option.SearchText)).GetData(_option, out var total).ToList();
        if (models.Count > 50000)
        {
            await ToastService.Warning("online Excel max data count 50000");
            return;
        }
        var uSheetDatas = await VariableServiceHelpers.ExportVariableAsync(models);

        op.Component = BootstrapDynamicComponent.CreateComponent<USheet>(new Dictionary<string, object?>
        {
             {nameof(USheet.OnSave), async (USheetDatas data) =>
            {
                try
    {
                await Task.Run(async ()=>
                {
              var importData=await  VariableServiceHelpers.ImportAsync(data);
                await    GlobalData.VariableRuntimeService.ImportVariableAsync(importData,AutoRestartThread, default);
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



    private async Task ExcelImportAsync(ITableExportContext<VariableRuntime> tableExportContext)
    {
        var op = new DialogOption()
        {
            IsScrolling = true,
            ShowMaximizeButton = true,
            Size = Size.ExtraLarge,
            Title = GatewayLocalizer["ImportVariable"],
            ShowFooter = false,
            ShowCloseButton = false,
            OnCloseAsync = async () =>
            {
                await InvokeAsync(table.QueryAsync);
            },
        };

        Func<IBrowserFile, Task<Dictionary<string, ImportPreviewOutputBase>>> preview = (a => GlobalData.VariableRuntimeService.PreviewAsync(a));
        Func<Dictionary<string, ImportPreviewOutputBase>, Task> import = (value => GlobalData.VariableRuntimeService.ImportVariableAsync(value, AutoRestartThread, default));
        op.Component = BootstrapDynamicComponent.CreateComponent<ImportExcel>(new Dictionary<string, object?>
        {
             {nameof(ImportExcel.Import),import },
            {nameof(ImportExcel.Preview),preview },
        });
        await DialogService.Show(op);

        _ = Task.Run(GlobalData.VariableRuntimeService.PreheatCache);
    }

    #endregion 导出

    #region 清空

    private async Task ClearAsync()
    {
        try
        {
            await Task.Run(async () =>
            {

                await GlobalData.VariableRuntimeService.DeleteVariableAsync(Items.Select(a => a.Id), AutoRestartThread, default);
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

    private async Task InsertTestDataAsync()
    {
        try
        {

            try
            {
                await Task.Run(() => GlobalData.VariableRuntimeService.InsertTestDataAsync(TestVariableCount, TestDeviceCount, SlaveUrl, AutoRestartThread, default));
            }
            finally
            {
                await InvokeAsync(async () =>
                {
                    await ToastService.Default();
                    await table.QueryAsync();
                    StateHasChanged();
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

    [Parameter]
    public bool AutoRestartThread { get; set; }
    [Parameter]
    public ChannelDeviceTreeItem SelectModel { get; set; }
    [Inject]
    [NotNull]
    public IStringLocalizer<ThingsGateway.Gateway.Razor._Imports>? GatewayLocalizer { get; set; }
    #endregion
}
