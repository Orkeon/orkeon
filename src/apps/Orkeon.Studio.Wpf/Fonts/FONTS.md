# Polices — Orkeon Studio

Les trois familles de la marque sont des Google Fonts libres (licence OFL).
WPF ne lit pas le WOFF2 : téléchargez les **TTF** et déposez-les dans ce dossier.

| Rôle | Famille | Téléchargement |
|---|---|---|
| Display (titres) | **Bricolage Grotesque** | https://fonts.google.com/specimen/Bricolage+Grotesque |
| Texte / UI | **Hanken Grotesk** | https://fonts.google.com/specimen/Hanken+Grotesk |
| Mono (code, labels) | **IBM Plex Mono** | https://fonts.google.com/specimen/IBM+Plex+Mono |

Les TTF **statiques** sont commis dans ce dossier (les variable fonts sont évitées :
WPF ne rend fiablement que leur instance par défaut, les graisses SemiBold/Bold
seraient synthétisées) :

```
Fonts/
  BricolageGrotesque-{Regular,SemiBold,Bold}.ttf     (upstream ateliertriay/bricolage)
  HankenGrotesk-{Regular,Medium,SemiBold,Bold}.ttf   (Google Fonts, sous-ensemble latin)
  IBMPlexMono-{Regular,Medium}.ttf                   (upstream IBM/plex 2.5.0)
  OFL-*.txt                                          (licences par famille)
```

⚠️ **Rendu** : les instances Hanken Grotesk sont **non hintées** (pas de tables
`fpgm`/`cvt`). La fenêtre doit donc rester en `TextOptions.TextFormattingMode="Ideal"`
(le défaut WPF, qui ignore le hinting) — le mode `Display` s'appuie sur le hinting
TrueType et rend ces polices crénelées. `Ideal` gère aussi mieux les tailles
fractionnaires du design (10.5, 11.5…).

## Intégration

1. Dans `Orkeon.Studio.Wpf.csproj` :

```xml
<ItemGroup>
  <Resource Include="Fonts\**\*.ttf" />
</ItemGroup>
```

2. Les `FontFamily` de `Themes/Studio.xaml` pointent déjà dessus :

```xml
<FontFamily x:Key="DisplayFont">./Fonts/#Bricolage Grotesque, Segoe UI</FontFamily>
<FontFamily x:Key="TextFont">./Fonts/#Hanken Grotesk, Segoe UI</FontFamily>
<FontFamily x:Key="MonoFont">./Fonts/#IBM Plex Mono, Consolas</FontFamily>
```

Tant que les TTF ne sont pas déposés, WPF retombe proprement sur Segoe UI / Consolas.
