# Task: Filmstrip thumbnails for recovered rows — done

Carved / disk-image-live / deleted rows had blank filmstrip cells (no host path).

## Done
- `RecoveredImageBytes.TryGet(row)` (App/Viewing) — central byte resolver dispatching to the
  existing readers (carved range, ReadFileBytes, ReadDeleted{File,Fat,ExFat}FileBytes).
- `MediaEntityRow.FilmstripThumbnail` (object?, notifies) — background-computed; typed object
  to keep Core WPF-free.
- `ShellViewModel.LoadDiskImageAsync`: after enumeration, decodes 60px thumbnails off-thread
  (sequential — each read mounts the image) for byte-source rows and sets the property.
- `FilmstripImageConverter` → `IMultiValueConverter` (row + FilmstripThumbnail); MainWindow.xaml
  filmstrip uses a MultiBinding so it refreshes when the thumbnail arrives. File rows unchanged.

## Verified
- Full solution builds on ext4 (0 errors). No new unit tests: the byte readers are already
  tested; the resolver is thin dispatch in App (no App.Tests project); the rendering is WPF
  and can't run headless on WSL.

## Follow-up
- Gallery/storyboard thumbnails for these rows; thumbnail disk caching; live exFAT.
- (Backlog) migrate WPF→Avalonia would let the App/viewer be tested on Linux.
