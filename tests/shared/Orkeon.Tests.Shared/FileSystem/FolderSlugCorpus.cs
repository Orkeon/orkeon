using Xunit;

namespace Orkeon.Tests.Shared.FileSystem;

/// <summary>
/// Names and the folder each one becomes (STUDIO-24), checked by every suite that turns a
/// name into a folder: the Domain rule itself, the CLI's forge session and Studio's adopted
/// team. The same name handed to the CLI or typed in Studio must land in the same folder, so
/// an entry here is a promise both sides keep.
/// <para>
/// Every name is one Studio's name field keeps as typed — one line, no Markdown, under the
/// 64-character cap — and keeps at least one ASCII letter or digit: a name that keeps none
/// falls back differently on each side by design (a timestamp for a session, the team
/// fallback for a team), so each suite pins its own.
/// </para>
/// </summary>
public static class FolderSlugCorpus
{
    /// <summary>The name, then the folder it becomes.</summary>
    public static TheoryData<string, string> Entries => new()
    {
        { "Veille fournisseurs", "veille-fournisseurs" },
        { "Résumer les offres, chaque matin !", "resumer-les-offres-chaque-matin" },
        { "  UPPER case  ", "upper-case" },
        { "Équipe d'été 2026", "equipe-d-ete-2026" },
        { "Crème brûlée & café", "creme-brulee-cafe" },
        { "Tri — courriels, pièces jointes", "tri-courriels-pieces-jointes" },
        { "Suivi des factures (T3 / 2026)", "suivi-des-factures-t3-2026" },
        { "Déjà_vu v2.1", "deja-vu-v2-1" },
        { "2026 : bilan annuel", "2026-bilan-annuel" },
        { "revue-contrats", "revue-contrats" },
    };
}
