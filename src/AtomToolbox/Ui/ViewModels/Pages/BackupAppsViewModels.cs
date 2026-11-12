using Atom.Toolbox.Application.Abstractions;
using Atom.Toolbox.Desktop.Ui;
using Atom.Toolbox.Desktop.ViewModels.Items;
using Atom.Toolbox.Domain.Maintenance;

namespace Atom.Toolbox.Desktop.ViewModels.Pages;

public sealed class BackupViewModel : PageViewModel
{
    private readonly IBackupService _backup;
    private readonly IEnvironmentService _environment;
    private readonly Action<string, string?> _toast;

    public BackupViewModel(IBackupService backup, IEnvironmentService environment, Action<string, string?> toast)
    {
        _backup = backup;
        _environment = environment;
        _toast = toast;
        Title = "BACKUP & RESTORE";
        ShowAdminWarning = !environment.IsElevated;
        Intro = "CREATE SYSTEM RESTORE POINTS AND EXPORT TWEAK-RELATED REGISTRY BRANCHES BEFORE MAKING BIG CHANGES.";
    }

    public override async Task LoadAsync()
    {
        IReadOnlyList<RestorePoint> points = [];
        IReadOnlyList<RegistryBackup> backups = [];
        try
        {
            points = await _backup.ListRestorePointsAsync();
            backups = await _backup.ListBackupsAsync();
        }
        catch (Exception ex)
        {
            Console.Write($"NOTE: {ex.Message}");
        }

        Build(points, backups);
    }

    private void Build(IReadOnlyList<RestorePoint> points, IReadOnlyList<RegistryBackup> backups)
    {
        Sections.Clear();

        var restoreRows = new List<object>
        {
            new ActionRowViewModel(
                "CREATE RESTORE POINT",
                "Creates an \"AtomToolBox\" checkpoint (temporarily bypasses the 24h creation limit).",
                "CREATE",
                CreatePointAsync),
            new ActionRowViewModel(
                "OPEN SYSTEM RESTORE",
                "Most recent points are listed below. Open System Restore to roll back.",
                "OPEN",
                () =>
                {
                    _environment.OpenShortcut("rstrui");
                    return Task.FromResult(new ActionOutcome(true, "OPENED"));
                }),
            new ActionRowViewModel(
                "EXISTING POINTS",
                $"{points.Count} restore point(s) found.",
                "REFRESH",
                RefreshAsync)
        };

        if (points.Count == 0)
        {
            restoreRows.Add(new MessageViewModel { Text = "NO RESTORE POINTS FOUND" });
        }
        else
        {
            foreach (var point in points.OrderByDescending(p => p.SequenceNumber))
            {
                restoreRows.Add(new MessageViewModel
                {
                    Text = $"#{point.SequenceNumber} — {point.Description}   ·   {point.CreatedAt.LocalDateTime:g}"
                });
            }
        }

        Sections.Add(new PageSectionViewModel
        {
            Title = "SYSTEM RESTORE POINTS",
            IsCard = true,
            Items = restoreRows
        });

        var backupRows = new List<object>
        {
            new ActionRowViewModel(
                "CREATE BACKUP",
                "Exports tweak-related branches: policies, power schemes, multimedia, explorer, control panel.",
                "BACKUP NOW",
                CreateBackupAsync),
            new ActionRowViewModel(
                "BACKUP FOLDER",
                _backup.BackupDirectory,
                "OPEN FOLDER",
                () =>
                {
                    _environment.OpenPath(_backup.BackupDirectory);
                    return Task.FromResult(new ActionOutcome(true, "OPENED"));
                })
        };

        if (backups.Count == 0)
        {
            backupRows.Add(new MessageViewModel { Text = "NO REGISTRY BACKUPS YET" });
        }
        else
        {
            foreach (var backup in backups)
            {
                backupRows.Add(new ActionRowViewModel(
                    backup.Name,
                    $"{DashboardViewModel.FormatBytes(backup.SizeBytes)} | {backup.CreatedAt.LocalDateTime:g}",
                    "RESTORE",
                    () => RestoreAsync(backup)));
            }
        }

        Sections.Add(new PageSectionViewModel
        {
            Title = "REGISTRY BACKUP",
            IsCard = true,
            Items = backupRows
        });
    }

    private async Task<ActionOutcome> CreatePointAsync()
    {
        Console.Write("Creating restore point \"AtomToolBox\"...");
        var result = await _backup.CreateRestorePointAsync();
        Console.Write(result.Ok ? "Restore point created." : $"ERROR: {result.Error}");
        if (result.Ok)
        {
            _toast("RESTORE POINT CREATED", "AtomToolBox");
            await LoadAsync();
        }
        else
        {
            _toast("RESTORE POINT FAILED", result.Error);
        }
        return new ActionOutcome(result.Ok, result.Ok ? "CREATED" : "FAILED");
    }

    private async Task<ActionOutcome> CreateBackupAsync()
    {
        Console.Write("Exporting registry branches...");
        var result = await _backup.CreateRegistryBackupAsync();
        Console.Write(result.Ok ? $"{result.Value?.Count ?? 0} branch(es) exported." : $"ERROR: {result.Error}");
        if (result.Ok)
        {
            _toast("REGISTRY BACKUP COMPLETE", $"{result.Value?.Count ?? 0} BRANCHES SAVED");
            await LoadAsync();
        }
        else
        {
            _toast("REGISTRY BACKUP FAILED", result.Error);
        }
        return new ActionOutcome(result.Ok, result.Ok ? "BACKED UP" : "FAILED");
    }

    private async Task<ActionOutcome> RefreshAsync()
    {
        await LoadAsync();
        return new ActionOutcome(true, "REFRESHED");
    }

    private async Task<ActionOutcome> RestoreAsync(RegistryBackup backup)
    {
        if (System.Windows.MessageBox.Show(
                $"Import {backup.Name} into the registry? This overwrites the exported branches.",
                "Atom ToolBox",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning) != System.Windows.MessageBoxResult.Yes)
        {
            return new ActionOutcome(true, "CANCELLED");
        }

        var result = await _backup.RestoreAsync(backup);
        Console.Write(result.Ok ? $"Registry backup restored: {backup.Name}" : $"ERROR: {result.Error}");
        _toast(result.Ok ? "BACKUP RESTORED" : "RESTORE FAILED", backup.Name);
        return new ActionOutcome(result.Ok, result.Ok ? "RESTORED" : "FAILED");
    }
}

public sealed class AppsViewModel : PageViewModel
{
    private readonly IAppInstallerService _installer;
    private readonly Action<string, string?> _toast;
    private string? _currentId;

    public AppsViewModel(IAppCatalog catalog, IAppInstallerService installer, Action<string, string?> toast)
    {
        _installer = installer;
        _toast = toast;
        Title = "APPLICATION INSTALLER";
        HasConsole = false;
        Toolbar.Add(new ActionButtonViewModel("CANCEL DOWNLOAD", new RelayCommand(Cancel)));

        foreach (var group in catalog.Apps.GroupBy(a => a.Category).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            Sections.Add(new PageSectionViewModel
            {
                Title = group.Key,
                Items = group.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(app => (object)new AppRowViewModel(app.Id, app.Name, () => InstallAsync(app)))
                    .ToArray()
            });
        }
    }

    private async Task<ActionOutcome> InstallAsync(AppCatalogEntry app)
    {
        _currentId = app.Id;
        ShowProgress = true;
        ProgressPercent = 0;
        ProgressText = $"DOWNLOADING {app.Id.ToUpperInvariant()}...";
        try
        {
            var progress = new Progress<AppInstallProgress>(update =>
            {
                if (!string.Equals(update.AppId, _currentId, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                ProgressPercent = update.Percent;
                ProgressText = update.Stage switch
                {
                    "DOWNLOADING" => $"DOWNLOADING {app.Id.ToUpperInvariant()}... {update.Percent}%",
                    "INSTALLING" => $"INSTALLING {app.Id.ToUpperInvariant()}...",
                    "DONE" => $"{app.Id.ToUpperInvariant()} INSTALLED",
                    "CANCELLED" => $"{app.Id.ToUpperInvariant()} CANCELLED",
                    "FAILED" => $"{app.Id.ToUpperInvariant()} FAILED",
                    _ => update.Stage
                };

                if (!string.IsNullOrWhiteSpace(update.Message))
                {
                    ProgressText += " — " + update.Message;
                }
            });

            var result = await _installer.InstallAsync(app, progress);
            if (result.Ok)
            {
                ProgressPercent = 100;
                _toast("INSTALL COMPLETE", app.Name);
            }
            else
            {
                _toast("INSTALL FAILED", result.Error);
            }

            return new ActionOutcome(result.Ok, result.Ok ? app.Name : "FAILED");
        }
        finally
        {
            _currentId = null;
            await Task.Delay(2600);
            ShowProgress = false;
            ProgressText = "";
        }
    }

    private void Cancel()
    {
        if (_currentId is not null)
        {
            _installer.Cancel(_currentId);
            ProgressText = $"CANCELLING {_currentId.ToUpperInvariant()}...";
        }
    }
}
