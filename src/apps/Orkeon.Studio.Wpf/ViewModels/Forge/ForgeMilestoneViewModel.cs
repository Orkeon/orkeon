using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Forge;

/// <summary>
/// The milestone banner (UX study §3): Décrire ▸ Proposer ▸ Essayer ▸ Adopter — an
/// indicator, never a navigation. Pure projection of <see cref="ForgeMilestone"/>.
/// </summary>
public sealed class ForgeMilestoneViewModel : ObservableObject
{
    private ForgeMilestone _current = ForgeMilestone.Describe;

    /// <summary>The milestone the cycle is on.</summary>
    public ForgeMilestone Current
    {
        get => _current;
        set
        {
            if (SetProperty(ref _current, value))
            {
                OnPropertiesChanged(
                    nameof(IsDescribeCurrent), nameof(IsDescribeDone),
                    nameof(IsProposeCurrent), nameof(IsProposeDone),
                    nameof(IsTryCurrent), nameof(IsTryDone),
                    nameof(IsAdoptCurrent));
            }
        }
    }

    /// <summary>Décrire is the active milestone.</summary>
    public bool IsDescribeCurrent => Current == ForgeMilestone.Describe;

    /// <summary>Décrire is behind us.</summary>
    public bool IsDescribeDone => Current > ForgeMilestone.Describe;

    /// <summary>Proposer is the active milestone.</summary>
    public bool IsProposeCurrent => Current == ForgeMilestone.Propose;

    /// <summary>Proposer is behind us.</summary>
    public bool IsProposeDone => Current > ForgeMilestone.Propose;

    /// <summary>Essayer is the active milestone.</summary>
    public bool IsTryCurrent => Current == ForgeMilestone.Try;

    /// <summary>Essayer is behind us.</summary>
    public bool IsTryDone => Current > ForgeMilestone.Try;

    /// <summary>Adopter is the active milestone.</summary>
    public bool IsAdoptCurrent => Current == ForgeMilestone.Adopt;
}
