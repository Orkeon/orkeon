> 🇬🇧 [English version](../../adr/ADR-017-transcript-display-model.md)

> **Voir aussi** : [Commandes TypeScript](../architecture/cli-ts-commands.md) · [Retour à l'index](../INDEX.md)

# ADR-017 — Le transcript peint par-dessus son texte, et seul le contenu peint un fond

**Statut** : Accepté · **Date** : 2026-11-05
· **Portée** : `Orkeon.Cli.TerminalGui` (`TranscriptBuffer`, `TranscriptView`, `SchemeFactory`), `Orkeon.Cli.Abstractions` (`IBlockConsole`), `Orkeon.Cli.Commands.Scripting` (`ctx.writeBlock`)

## Contexte

Le transcript de la console à deux volets stockait du texte brut : rien de ce qu'on y écrivait ne
pouvait porter de couleur, ni un prompt soumis, ni un diff, ni du markdown rendu. La mise en page avait
aussi une règle, chaque attribut garde le fond du terminal et une ligne porte un seul rôle vif, pour ne
jamais contrarier le thème de l'utilisateur. Un diff a besoin de fonds, et un script doit pouvoir en
demander un sans rien savoir des couleurs.

## Décision

1. **Le texte d'une ligne reste la seule source** pour le retour à la ligne, les positions, la sélection
   et la coupe. Une ligne stylée ajoute des plages triées, un rôle peint sur toute la rangée, un retrait
   suspendu et une source de copie facultative. Seul le retrait atteint le retour à la ligne.
2. **Seul le contenu peint un fond** : un prompt soumis, une ligne de diff, un mot modifié. La mise en
   page autour garde le fond du terminal. Un rôle à fond fixe toujours aussi sa couleur de texte : il se
   lit de la même façon sur un terminal sombre et sur un clair, sans savoir lequel il a devant lui.
3. **La copie d'une ligne entière rend sa source** quand elle en a une ; la copie d'une partie rend ce
   qui est affiché. Relier chaque caractère affiché à sa source ne vaut pas ce que cela coûte.
4. **Un script donne une source et un genre, jamais une couleur.** `ctx.writeBlock({ kind, text })`
   atteint une console qui implémente `IBlockConsole`, et toute autre console écrit `text` en lignes
   simples. Le modèle stylé reste interne à `Orkeon.Cli.TerminalGui` : le rendu se fait là, les plages ne
   traversent donc aucune interface publique et les scripts ne peuvent pas en dépendre.
5. **Les lignes stylées prennent la ligne vide laissée ouverte après un dernier saut de ligne et en
   ouvrent une nouvelle** : elles n'ajoutent aucune rangée vide, et le flux écrit ensuite ne les prolonge
   jamais.

## Conséquences

- Les sorties simple et de replay ne changent pas : `--ui plain` montre la même source qu'avant.
- Rendre un bloc coûte le temps du rendu sur le fil d'interface, à chaque bloc écrit.
- Une sélection partielle d'une ligne rendue copie le texte rendu, pas la source.

## Solutions écartées

- **Des séquences ANSI dans le texte.** Le retour à la ligne, les positions et la sélection devraient
  toutes les sauter, et un script pourrait imprimer des couleurs que la sortie simple montrerait comme
  du bruit.
- **Un type public de plages stylées pour les scripts.** Il figerait le modèle d'affichage dans l'API
  publique et obligerait chaque script à choisir des couleurs.
