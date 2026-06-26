using System;
using System.Collections.Generic;
using MpuTrainer.Models;
using MpuTrainer.ViewModels;

namespace MpuTrainer.Services;

// ============================================================
//  Navigation zwischen den Ansichten
// ============================================================

public interface INavigationService
{
    /// <summary>Wird ausgeloest, wenn ein neues Ziel-ViewModel aktiv wird.</summary>
    event Action<ViewModelBase>? CurrentChanged;

    /// <summary>Navigiert zu einem ueber DI aufgeloesten ViewModel.</summary>
    void NavigateTo<TViewModel>() where TViewModel : ViewModelBase;
}

/// <summary>
/// Loest Ziel-ViewModels ueber den DI-Container auf und benachrichtigt das
/// MainViewModel ueber den Wechsel. Die Ansichten werden pro Projekt
/// zwischengespeichert, damit beim Wechseln der Kacheln nichts verloren geht;
/// bei einem Projektwechsel wird der Zwischenspeicher geleert.
/// </summary>
public class NavigationService : INavigationService
{
    private readonly IServiceProvider _services;
    private readonly Dictionary<Type, ViewModelBase> _cache = new();

    public NavigationService(IServiceProvider services, IAppSession session)
    {
        _services = services;
        session.CurrentProjectChanged += () => _cache.Clear();
    }

    public event Action<ViewModelBase>? CurrentChanged;

    public void NavigateTo<TViewModel>() where TViewModel : ViewModelBase
    {
        var type = typeof(TViewModel);
        if (!_cache.TryGetValue(type, out var vm))
        {
            vm = (ViewModelBase)_services.GetService(type)!;
            _cache[type] = vm;
        }
        CurrentChanged?.Invoke(vm);
    }
}

// ============================================================
//  Sitzung: aktuell geoeffnetes Projekt
// ============================================================

public interface IAppSession
{
    ClientProject? CurrentProject { get; set; }
    event Action? CurrentProjectChanged;
}

/// <summary>Haelt das aktuell geoeffnete Projekt prozessweit vor.</summary>
public class AppSession : IAppSession
{
    private ClientProject? _current;

    public ClientProject? CurrentProject
    {
        get => _current;
        set
        {
            _current = value;
            CurrentProjectChanged?.Invoke();
        }
    }

    public event Action? CurrentProjectChanged;
}
