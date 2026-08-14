# Polices — Orkeon Studio

Les trois familles de la marque sont des Google Fonts libres (licence OFL).
WPF ne lit pas le WOFF2 : téléchargez les **TTF** et déposez-les dans ce dossier.

| Rôle | Famille | Téléchargement |
|---|---|---|
| Display (titres) | **Bricolage Grotesque** | https://fonts.google.com/specimen/Bricolage+Grotesque |
| Texte / UI | **Hanken Grotesk** | https://fonts.google.com/specimen/Hanken+Grotesk |
| Mono (code, labels) | **IBM Plex Mono** | https://fonts.google.com/specimen/IBM+Plex+Mono |

Fichiers attendus (les variables-fonts TTF suffisent) :

```
Fonts/
  BricolageGrotesque[opsz,wdth,wght].ttf
  HankenGrotesk[wght].ttf
  IBMPlexMono-Regular.ttf
  IBMPlexMono-Medium.ttf
```

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
