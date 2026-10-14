# Accent swatch selection

The shared `Button.Selected` style replaced `PART_ContentPresenter` backgrounds with the neutral selection brush. Accent swatches now have a dedicated fill presenter bound to the actual swatch brush, and a separate selected-state border. The fill stays 24px in diameter; the 2px selection ring has a 32px outer diameter, leaving a 2px gap from the fill. The button now reserves the full 32px cell so its clipping bounds contain the ring.

The explicit Blue button is removed. Default retains its existing blue color. Saved `AccentColor.Blue` settings resolve to Default and select the Default swatch, so the removed option cannot leave the picker without a selected item. The underlying Blue enum/resource remains available for compatibility and other UI consumers.

Light/dark `AccentSwatchTests` passed (2 tests): eight visible options, no Blue button, circular geometry, visible selection rings with a 2px gap, preserved selected fill, and preserved hovered fill. The hover check scrolls the appearance row into view before injecting input. The focused test build uses the existing temporary exclusion of unrelated stale picker tests; the repository test project is unchanged. The full test suite was not run.

`git diff --check` passed. Physical visual acceptance remains user verification. Already-running standalone Settings processes retain their loaded UI until the window/application is reopened.

Native application builds and shell reload completed successfully. Strict compositor health check reported zero required failures and zero warnings.
