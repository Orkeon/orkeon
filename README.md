# The Orkeon apt repository

This branch is managed by CI — do not modify it by hand, and never force-push it: its
history is the log of every publication.

Each directory is a channel (`stable`, `rc`, `dev`) holding a signed flat apt index
(`InRelease`, `Release`, `Release.gpg`, `Packages`, `Packages.gz`, `by-hash/`). The packages
themselves are the assets of the GitHub Releases; `orkeon-archive-keyring.gpg` is the
repository's public key.

To install Orkeon with apt, follow
[Install with apt](https://github.com/Orkeon/orkeon/blob/main/docs/guides/install-with-apt.md).
