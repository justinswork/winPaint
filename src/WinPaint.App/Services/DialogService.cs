using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using WinPaint.App.Resources;
using WinPaint.App.ViewModels;
using WinPaint.App.Views;
using WinPaint.Core.Imaging;
using WinPaint.Core.Imaging.Codecs;

namespace WinPaint.App.Services;

/// <summary>WPF implementation of <see cref="IDialogService"/>.</summary>
public sealed class DialogService(Window owner, SettingsService settings) : IDialogService
{
    /// <summary>Applies a theme chosen in the Settings dialog.</summary>
    public Action<AppTheme>? ThemeSetter { get; set; }

    /// <inheritdoc/>
    public string? PickOpenFile()
    {
        var dlg = new OpenFileDialog { Filter = ImageFormats.OpenFilter, CheckFileExists = true };
        return dlg.ShowDialog(owner) == true ? dlg.FileName : null;
    }

    /// <inheritdoc/>
    public (string Path, ImageFormat Format)? PickSaveFile(string suggestedName, ImageFormat format)
    {
        var order = ImageFormats.SaveFilterOrder;
        var dlg = new SaveFileDialog
        {
            Filter = ImageFormats.SaveFilter,
            FilterIndex = Math.Max(0, order.ToList().IndexOf(format)) + 1,
            FileName = Path.GetFileNameWithoutExtension(suggestedName),
            AddExtension = true,
            DefaultExt = ImageFormats.DefaultExtension(format),
            OverwritePrompt = true,
        };
        if (dlg.ShowDialog(owner) != true)
        {
            return null;
        }

        var chosen = order[Math.Clamp(dlg.FilterIndex - 1, 0, order.Count - 1)];
        var byExt = ImageFormats.FromPath(dlg.FileName);
        var path = dlg.FileName;
        if (byExt is null)
        {
            path += ImageFormats.DefaultExtension(chosen);
        }

        return (path, byExt ?? chosen);
    }

    /// <inheritdoc/>
    public SaveChoice AskSaveChanges(string documentName)
    {
        var msg = string.Format(CultureInfo.CurrentCulture, Strings.Prompt_SaveChanges, documentName);
        return MessageDialog.Show(owner, Strings.AppName, msg, [Strings.Prompt_Save, Strings.Prompt_DontSave, Strings.Cancel], 0, 2) switch
        {
            0 => SaveChoice.Save,
            1 => SaveChoice.DontSave,
            _ => SaveChoice.Cancel,
        };
    }

    /// <inheritdoc/>
    public bool Confirm(string message) => MessageDialog.Show(owner, Strings.AppName, message, [Strings.OK, Strings.Cancel], 0, 1) == 0;

    /// <inheritdoc/>
    public void ShowError(string message) => MessageDialog.Show(owner, Strings.AppName, message, [Strings.OK], 0);

    /// <inheritdoc/>
    public void Info(string message) => MessageDialog.Show(owner, Strings.AppName, message, [Strings.OK], 0);

    /// <inheritdoc/>
    public ResizeSkewResult? ResizeSkew(int width, int height)
    {
        var vm = new ResizeSkewViewModel(width, height);
        return new ResizeSkewDialog(vm) { Owner = owner }.ShowDialog() == true ? vm.Result : null;
    }

    /// <inheritdoc/>
    public ImagePropertiesResult? ImageProperties(ImagePropertiesInfo info)
    {
        var vm = new ImagePropertiesViewModel(info);
        if (new ImagePropertiesDialog(vm) { Owner = owner }.ShowDialog() != true)
        {
            return null;
        }

        if (vm.BlackAndWhite && !Confirm(Strings.Msg_BlackWhiteConfirm))
        {
            return new ImagePropertiesResult(vm.ResultWidth, vm.ResultHeight, false);
        }

        return new ImagePropertiesResult(vm.ResultWidth, vm.ResultHeight, vm.BlackAndWhite);
    }

    /// <inheritdoc/>
    public Color? EditColors(Color initial, Action<Color> addCustom)
    {
        var vm = new EditColorsViewModel(initial);
        return new EditColorsDialog(vm, addCustom) { Owner = owner }.ShowDialog() == true ? vm.NewColor : null;
    }

    /// <inheritdoc/>
    public IReadOnlyList<int>? PickIcoSizes() => IcoSizesDialog.Show(owner);

    /// <inheritdoc/>
    public void ShowShortcuts() => ShortcutsDialog.Show(owner);

    /// <inheritdoc/>
    public void ShowSettings() => SettingsDialog.Show(owner, settings, t => ThemeSetter?.Invoke(t));

    /// <inheritdoc/>
    public bool PageSetup() => PageSetupDialog.Show(owner, settings);

    /// <inheritdoc/>
    public void Print(PixelBuffer image, double dpiX, double dpiY, string jobName) => Printing.Print(owner, settings, image, dpiX, dpiY, jobName);

    /// <inheritdoc/>
    public void PrintPreview(PixelBuffer image, double dpiX, double dpiY, string jobName) => Printing.Preview(owner, settings, image, dpiX, dpiY, jobName);
}
