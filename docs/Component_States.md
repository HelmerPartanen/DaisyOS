# DaisyOS shared component contract

`Styles/ComponentStyles.axaml` owns common control states. `Styles/DropdownMenuStyles.axaml` owns popup, menu, ComboBox, and menu-item behavior. Feature views may compose these roles but must not redefine their semantic colors, target sizes, typography, focus rings, or motion.

## State matrix

| Family | Default | Hover | Focus visible | Active/pressed | Disabled | Loading | Error | Selected |
|---|---|---|---|---|---|---|---|---|
| Primary, secondary, ghost, danger buttons | Semantic surface and content | Shared hover brush | 2px focus ring | Shared pressed brush | Dimmed and inert | `Loading`, inert with progress cursor | Danger variant | `Selected` or checked surface |
| Text input and search | Input surface and default stroke | Strong stroke | Stable focus ring | Native text editing | Disabled content and sunken surface | Host uses `LoadingState` | `Error` danger stroke | Selection remains native |
| Check, radio, and switch | Standard text and target | Native state colors | Shared 2px focus ring | Native checked/pressed state | Dimmed | Host uses `LoadingState` | Host uses feedback state | Checked state |
| List rows and tabs | 32px target | Hover surface | Shared 2px focus ring | Pressed surface | Dimmed | Host uses `LoadingState` | Host uses feedback state | Selected surface |
| ComboBox and menu items | 32px field / 32px row | Hover surface | Shared 2px focus ring | Pressed surface | Dimmed | Host uses `LoadingState` | Validation/feedback host | Selected surface |
| Cards and feedback | Raised or sunken surface | Child controls respond | Child controls show focus | No decorative press | Content controls dim | `LoadingState` | `FeedbackState Error` | Selected content owns state |
| Tooltip | Overlay surface and caption type | Not interactive | Not focusable | Not interactive | Hidden with owner | Not applicable | Not applicable | Not applicable |
| Progress | Secondary track, primary fill | Not interactive | Not focusable | Value updates immediately | Owner determines visibility | Indeterminate or determinate | Pair with error host | Not applicable |

## Composition rules

- Default desktop controls use 32px targets. Dense toolbars and menus use the explicit `Compact` 32px role. Touch-oriented primary actions use `Comfortable` at 44px.
- HugeIcons is the control icon set. Icons use the 12/16/20/24/32px scale; raster images are reserved for application icons, artwork, thumbnails, and user content.
- Empty, loading, and error layouts use `EmptyState`, `LoadingState`, and `ErrorState`. Recovery actions appear immediately below the explanation.
- Press scaling transforms the entire button around its center; labels do not receive a second transform.
- Keyboard focus uses `:focus-visible`. Hover never creates a focus ring.
- Feature-specific variants remain only when their geometry communicates the feature: calculator keys, window caption buttons, screenshot handles, calendar cells, taskbar items, console game tiles, and direct-manipulation surfaces.
- Window frames, title bars, notifications, and toasts keep specialized structure because they expose compositor, drag, urgency, or timing behavior. Their colors, type, icon sizing, radii, and motion still resolve through the shared tokens.
