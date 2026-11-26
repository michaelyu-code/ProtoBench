# Verification record

## Version 2.0.32 release checks — 26 November 2026

Keeps the first editable line at the top when either Text Compare pane is empty, avoiding alignment space that pushes the caret and the next paste below the visible input. Includes regression tests for both empty-pane positions. Bumped package/file version to 2.0.32 while retaining assembly identity 2.0.0.0.

The fix and its tests were included in this session's successful full Release run (852 passed, one opt-in large-file test skipped). The versioned x64 desktop Release build passed with zero warnings/errors and reports file version 2.0.32.0. Installer packaging and publication run in GitHub Actions using the exact tagged main-build artifacts.

## Version 2.0.31 release checks — 26 November 2026

Added SDK menu placement with custom-tool badges, a Custom Tools default and fallback for unknown or retired menu categories, and optional self-contained HTML screens hosted by WebView2. Web messages use the existing per-tab session validation, execution, cancellation and disposal paths. Updated the developer guide and samples. Bumped package/file version to 2.0.31 while retaining SDK assembly identity 2.0.0.0.

The full local Release suite passed across all four test projects, with the existing opt-in 2 GiB test skipped. The x64 desktop Release build passed with zero warnings/errors. The native/HTML counter sample compiled against the updated SDK, and `git diff --check` passed. Live custom-screen rendering and badge appearance remain unverified. Installer packaging and publication run in GitHub Actions using the exact tagged main-build artifacts.

## Version 2.0.28 release checks — 25 November 2026

Reduced the Open, Save as, Copy and encoding/newline controls above both Text Compare panes to 13-DIP text, a 28-DIP minimum height and smaller padding. Bumped package/file version to 2.0.28 while retaining assembly identity 2.0.0.0.

The full Release suite passed 822 tests, with the existing opt-in 2 GiB test skipped. The x64 desktop Release build passed with zero warnings/errors and reports file version 2.0.28.0. All 13 release build-selection checks and `git diff --check` passed. Installer packaging and publication run in GitHub Actions using the exact tagged main-build artifacts.

## Version 2.0.27 release checks — 25 November 2026

Bumped package/file version to 2.0.27 while retaining assembly identity 2.0.0.0. This release includes Text Compare paste-focus and redraw fixes, font-size selection with 12 as the default, position-aware overview navigation, faster scroll forwarding and linked line-number highlights.

The full Release suite passed 822 tests (Utilities 347, Parsing 161, Application 303 and Generation 11), with the existing opt-in 2 GiB test skipped. The x64 desktop Release build passed with zero warnings/errors and reports file version 2.0.27.0. Editor JavaScript syntax and all 13 release build-selection checks passed. Installer packaging and publication run in GitHub Actions using the exact tagged main-build artifacts.

## Linked line-number highlights — 25 November 2026

Clicking a source line number toggles a full-width blue row highlight across the two comparison panes. Corresponding rows use the comparison alignment rather than assuming equal source numbers; missing rows show the highlight in their aligned spacer gap. A different line replaces the highlight, clicking either corresponding number again clears it, and editing clears it. Filtered reviews expose the same number-click action and row highlight. The overlay does not modify source text, selection or undo history.

Headless Edge checks clicked an actual line number in a 40,000-line document and verified the bridge event, full-width overlay, matching gap overlay in the second editor, persistence after scrolling away/back, clearing and unchanged source/revision. Existing paste, font, navigation and fast-scroll checks passed. All 19 Text Compare Application tests passed. The x64 Release desktop build passed with zero warnings/errors.

## Faster synchronized Text Compare scrolling — 25 November 2026

Scroll positions now use a dedicated lightweight bridge message from a capturing scroll listener, before CodeMirror renders the source viewport. Previously synchronization waited for an animation frame, highlight painting and visible-line measurement. Native scroll forwarding no longer performs redundant gutter layout; the normal viewport update handles that work. Programmatic follower scrolls remain suppressed to prevent feedback.

Headless Edge checks verified immediate scroll messages during scroll-event dispatch before any viewport message, 100 rapid updates retaining the latest position, and a second editor reaching that position without echoing scroll messages. Existing large-paste, font-size, navigation and two-pane insertion checks passed. All 19 Text Compare Application tests passed; the x64 Release desktop build passed with zero warnings/errors. These checks validate the forwarding path rather than measuring end-to-end latency in the native WebView2 app.

## Position-aware comparison overview clicks — 25 November 2026

Clicking within an overview mark now targets the corresponding row within that change, rather than always its first row. This allows top/middle/bottom navigation through a single large changed block. Editable panes map to aligned rows; filtered reviews map to displayed rows using their measured heights, including wrapping. Previous/Next still targets the start of a changed section.

All 19 Text Compare Application tests passed, including six new cases covering a 40,000-row single change at top/middle/bottom, separated changes with context, wrapped filtered rows, tiny expanded marks, endpoint clamping and missing/invalid targets. The x64 desktop Release build passed with zero warnings/errors.

## Text Compare change navigation — 25 November 2026

Comparison jumps now position the selected change about one third down the editor and suppress scroll feedback during the move. The host reveals the side containing the change, reads its resulting scroll position and aligns the other pane to that position, rather than sending a missing source line to the end of the document. Stacked panes use the change's source insertion offset when a line is absent. Superseded asynchronous jumps do not reposition the other pane.

Headless Edge checks jumped forward/backward to lines 15,000, 1,000, 39,000, 1 and 20,000 in a 40,000-line document, verifying visibility, predictable positioning, stable scroll offsets, bounded rendering and no user-scroll feedback. A two-editor insertion check verified that the other pane displays the matching alignment gap. Paste-focus, immediate-redraw and font-size checks passed. All 13 Text Compare Application tests passed; the x64 Release desktop build passed with zero warnings/errors.

## Text Compare font size — 25 November 2026

Added View → Font size with 12, 14, 16, 18 (default), 20, 22, 24, 28 and 32 sizes, applied to both editors and filtered review text/line numbers. Font changes refresh editor geometry and alignment; filtered rows use the selected size for text measurement, row-height estimates and consistent number-column widths.

Headless Edge checks on a 40,000-line document verified sizes 12, 32 and reset to 18, including computed font/line height, unchanged source and selection/revision, visible text and bounded rendering. Existing paste-focus and immediate-redraw browser checks passed. The x64 Release desktop build passed with zero warnings/errors; editor syntax and `git diff --check` passed.

## Text Compare redraw after large paste — 25 November 2026

Refresh CodeMirror after changing alignment widgets. Removing a large offscreen spacer could leave the rendered lines at their old pixel offset while the editor retained the same visible line range, producing a blank pane until scrolling.

An isolated headless Edge regression check reproduced the blank pane after a 40,000-line paste into an input with a large leading gap. Without the refresh, the immediate-visibility assertion failed. With the fix, source lines intersected the viewport without further scrolling, the pasted source matched exactly, the removed gap no longer inflated document height, the view stayed near the pasted caret, and fewer than 100 lines were rendered. Both earlier gap-focus checks also passed. The x64 desktop Release build passed with zero warnings/errors; JavaScript syntax and `git diff --check` passed.

## Text Compare paste focus — 25 November 2026

Alignment spacer widgets now let CodeMirror handle mouse events. Previously, clicking the blank gap above an empty input (after pasting into the other pane) did not focus the editor, preventing subsequent paste into that pane. The same fix covers gaps in shorter inputs.

An isolated headless Edge check using the bundled editor reproduced the focus failure with the previous widget setting. With the fix, clicks inside both leading empty-pane gaps and trailing shorter-pane gaps focused the editor, and browser text insertion reached the document while retaining existing content. All 13 Text Compare Application tests passed in Release. Editor JavaScript syntax and `git diff --check` passed.

## Version 2.0.26 release checks — 25 November 2026

Bumped package/file version to 2.0.26 while retaining assembly identity 2.0.0.0. The full Release suite passed 816 tests with the existing opt-in 2 GiB test skipped. The isolated x64 desktop Release build passed with zero warnings/errors and reports file version 2.0.26.0. Bundled editor assets were copied to the output and editor JavaScript passed its syntax check. All 13 offline release-selection checks passed and both GitHub Actions workflows passed actionlint 1.7.12. Installer packaging and publication run in GitHub Actions using the single main build.

## Continuous virtualized Text Compare — 25 November 2026

Replaced the RichEditBox display with locally bundled CodeMirror 5.65.21 in WebView2. Each pane retains one continuous editable document and scrollbar. CodeMirror renders only the viewport plus a 12-line margin; highlight spans are also restricted to that viewport. Edits cross the host bridge as versioned patches instead of repeatedly sending the whole document. Comparison, file saving, selection/copy, undo/redo and Find use the full source. Paragraph alignment uses line widgets and measured natural heights, including wrapped rows. The rejected paging implementation and its controls have been removed.

The full Release suite passed 816 tests: Utilities 347, Parsing 161, Application 297 and Generation 11, with the existing opt-in 2 GiB test skipped. Four virtual-layout tests cover 30,000 equal lines, inserted lines, leading/trailing gaps and wrapped heights; the superseded native-spacing tests were removed with that implementation. The desktop Release build passed with zero warnings/errors. Live rendering counters reported 28 rendered rows near the start and 30 near the end of a complete 30,000-line HTML document (3,337,779 characters). Ctrl+End displayed readable text at line 30,000 without paging. A final-line edit and undo followed by Save as exactly reproduced the full original source. Ctrl+F found an offscreen match around line 29,175. The final build also preserves the active pane's scroll position when comparison layout refreshes. Included in v2.0.26.

## Version 2.0.25 release checks — 25 November 2026

Bumped package/file version to 2.0.25 while retaining assembly identity 2.0.0.0. Text Compare now calculates alignment independently of scroll position and uses paragraph spacing instead of excessive bottom padding, preventing text from disappearing when one pane is empty or shorter. Unwrapped alignment plans only the required gaps, highlighting formats visible text, caret navigation uses a line index, and gutter/layout updates avoid continuous polling and redundant pane rebuilds. Live checks verified a 10,000-line input beside an empty pane and navigation/alignment around an insertion at line 5,000.

The full Release suite passed 825 tests: Utilities 347, Parsing 161, Application 306 and Generation 11. The existing opt-in 2 GiB test was skipped. The isolated x64 Release desktop build passed with zero warnings/errors and reports file version 2.0.25.0. All 13 offline release-selection checks passed, both GitHub Actions workflows passed actionlint 1.7.12, and `git diff --check` passed. Main pushes now build/package once; the tag-only release workflow waits for and reuses successful main-build artifacts for the exact tagged commit and version. Installer packaging/publication run in GitHub Actions.

## Version 2.0.24 release checks — 24 November 2026

Bumped package/file version to 2.0.24 while retaining assembly identity 2.0.0.0. This release fixes long-line overflow in filtered Text Compare panes, adds shared horizontal scrolling for those panes, and aligns overview markers with visible rows and measured wrapped heights.

The full Release suite passed 812 tests: Utilities 347, Parsing 161, Application 293 and Generation 11. The existing opt-in 2 GiB test was skipped. The isolated x64 Release desktop build passed with zero warnings/errors and reports file version 2.0.24.0. Installer/portable packaging and GitHub publication are handled by the tagged CI workflow.

## Filtered comparison overview mapping — 24 November 2026

The overview now maps the filtered review's displayed rows rather than source-row positions, and uses measured row heights when text wraps. List padding is measured from the first rendered row. Hidden rows no longer offset markers, wrapped sections retain their displayed proportions, and resizing refreshes the mapping. Drawing and click-to-jump share the same geometry.

The Application suite passed 293 tests, with the existing opt-in large-file test skipped. Four new overview tests cover hidden-row removal, wrapped/context heights, long-document compression, hit testing and clipping. The isolated desktop build passed with zero warnings/errors. Live checks verified two separated XML changes in Differences only, wrapped heights, selecting the second change by clicking its marker, and alignment after maximizing the window.

## Filtered Text Compare long-line containment — 24 November 2026

Fixed long XML/text lines widening filtered comparison rows and pushing the modified pane aside. Both panes now retain equal viewport widths and clip text within their own cells. A shared horizontal scrollbar moves the text in both panes while line numbers and the centre gutter stay fixed. Text is measured independently of the row width; wrapped rows track their text height and keep both sides aligned. Recycled row subscriptions are released when the filter or comparison changes.

The isolated x64 Debug desktop build passed with zero warnings/errors. Live Windows checks used long XML-style lines in Differences only, verified containment at the beginning/middle/end of horizontal scrolling, confirmed both line endings were reachable, and checked wrapping and maximized-window reflow. No comparison-engine or source-text behavior changed.

## Version 2.0.23 release checks — 24 November 2026

Bumped package/file version to 2.0.23 while retaining assembly identity 2.0.0.0. This release includes the Text Compare alignment, navigation, reconciliation, file handling and compact layout improvements below, plus the ISO 8583 tree preview in `work/iso8583-tree-preview.html`.

The full Release suite passed 808 tests: Utilities 347, Parsing 161, Application 289 and Generation 11. The existing opt-in 2 GiB test was skipped. The isolated x64 Release desktop build passed with zero warnings/errors. Installer/portable packaging and GitHub publication are handled by the tagged CI workflow.

## Text Compare interface and reconciliation — 24 November 2026

Previous/Next and the current-change indicator share the bottom summary row with section/line counts. Find/replace sits in the main toolbar, removing the extra top navigation row. Footer controls wrap in narrow layouts.

The overview strip follows the text editor's actual bounds below the file header (or the filtered review's list bounds), and clips marks to that area. Header reflow and editor resizing update its bounds.

Short documents now keep overview marks at line height instead of stretching them across the available editor height; long documents still compress to fit. Click-to-jump uses the same scale. Bounds are rechecked after layout settles. A live check reproduced the two-line numeric example with only the first line changed, and verified a compact blue marker aligned with the text box in both normal and maximized windows. The isolated desktop build passed with zero warnings/errors.

Added source-preserving line alignment, native editor spacer gaps, selected-change navigation (F7 / Shift+F7), a clickable overview, copy-change controls, Undo copy, debounced automatic comparison and cancellation of stale work. Rules ignore case, whitespace or blank lines without altering either input. Rules-aware HTML reports retain both inputs and highlight meaningful differences. View groups wrapping, scroll synchronization, whitespace markers and virtualized differences-only / three-line context review. Filtered review is read-only; All lines retains both editable native text panes. Ctrl+Z / Ctrl+Y use bounded text-only history so decoration changes do not consume undo steps.

Added per-pane Open, Save as, file drop and Copy, filename/unsaved indicators, strict encoding/BOM detection, explicit encoding selection and newline conversion. File writes use the existing atomic writer. Mixed line endings round-trip untouched until editing; edits use the first convention. Find/replace searches either full input with case-insensitive literal matching.

The Utilities suite passed 347 tests and Application passed 289 tests; the existing opt-in 2 GiB test was skipped. Coverage includes random/repeated-line alignment and source reconstruction, insertions/deletions, Unicode separators, bounded fallback, copy ranges in both directions, undo after recomparison/rule changes, rule-aware reports, malformed encodings and exact UTF-8/16/32 BOM/newline round trips. The desktop x64 Debug build passed with zero warnings/errors. Git whitespace checks passed.

Live Windows checks exercised editable alignment, a wrapped replacement with an inserted line, automatic refresh, differences-only review with character emphasis, change navigation, copying the selected change, Undo copy and Ctrl+Z / Ctrl+Y after highlighting. Native hidden-paragraph formatting was rejected by the editor; filtered review therefore uses a separate virtualized surface and leaves source editors untouched. File-picker saves, drag/drop, find/replace and narrow-window resizing remain manual interaction checks.

## Comparison report exports — 24 November 2026

Bumped the package/file version to 2.0.22 while retaining assembly identity 2.0.0.0.

The full Release suite passed 789 tests (Utilities 333, Parsing 161, Application 284 and Generation 11); the existing opt-in large-file test was skipped. The desktop Release build passed with zero warnings/errors.

Added Export differences report to Message Compare and Text Compare. Standalone HTML reports retain highlighted text changes or filtered message rows, with summaries, spec/filter scope and parser diagnostics where applicable. Report values are HTML-encoded, missing and empty values remain distinct, and exports capture the result before the save dialog opens. Buttons are disabled without a current comparison or during comparison/export, and cancellation and save errors are handled in the view.

All 20 focused comparison-report and Message Compare workspace tests passed. Coverage includes active filters, identical/empty text, stale results, markup escaping, multiline values and missing/empty fields. The desktop Debug build passed with zero warnings/errors. Live save-dialog and browser/print layout checks remain manual.

## Version 2.0.21 release checks — 24 November 2026

Bumped package/file version to 2.0.21 while retaining SDK assembly identity 2.0.0.0. Text Compare now offers Unified scrolling, checked by default, with independent scrolling when unticked. Removed the 100,000-character limit from both editors and the comparison engine; large replacements use whole-block highlights without discarding input. Updated the README and tool help.

The full Release suite passed 785 tests: Utilities 333, Parsing 161, Application 280 and Generation 11. The existing opt-in large-file test was skipped. The Release desktop build passed with zero warnings/errors; its DLL reports file version 2.0.21.0. `git diff --check` passed. Live scrolling and complete 3,000-line paste/comparison checks are recorded below. Installer/portable packaging and GitHub publication are handled by the tagged CI workflow.

## Text Compare input limit removal — 24 November 2026

Removed the 100,000-character editor and comparison-engine limits. Inputs no longer have a fixed app-imposed character or line cap. Large one-sided changes retain their full text without grapheme expansion; replacements exceeding 200,000 combined characters use the existing whole-block highlight behavior instead of allocating a string for every grapheme. Updated help and README.

All 19 TextComparison tests passed, including 6,000-line inputs exceeding the previous limit with a precise change near the end, identical large inputs, million-character additions/removals/replacements, full reconstruction and cancellation. The desktop Debug build to `artifacts/textcompareunlimitedcheck` passed with zero warnings/errors. Live checks pasted all 3,000 lines (320,999 characters) into the original pane and the same text plus a final addition into the modified pane, completed comparison and showed the added text highlighted on line 3,000. The separate large-paste test window was closed afterward.

## Text Compare unified scrolling — 24 November 2026

Added a Unified scrolling checkbox, checked by default, linking the editors' horizontal and vertical scroll offsets. Unticking restores independent scrolling; re-enabling aligns the modified pane to the original. Each editor clamps to its own scroll range, with follower scroll events suppressed until completion to avoid pulling the source back when text lengths differ. Scroll handlers detach when editors unload. Updated the tool help and README.

The desktop Debug build passed with zero warnings/errors. Live checks with 100 original lines and 70 modified lines confirmed scrolling upward from either pane moves both to the same visible line, unticking permits independent positions, re-enabling aligns them, and scrolling the longer text beyond the shorter text's bottom does not pull it back. Scrollbar-drag verification was interrupted by detected user input; horizontal scrolling, wrapped long lines and narrow-window layouts remain manual checks.

## Version 2.0.20 release checks — 24 November 2026

Bumped package/file version to 2.0.20 while retaining SDK assembly identity 2.0.0.0. Message Compare's field picker keeps a stable size while searching, with a smaller fixed-height search box and the explanatory blurb removed. Log Parser reopens input at full workspace height with a larger editor, visible scrollbars and a fixed Back to results button. Reparse file now appears only in the Open file tab.

The full Release suite passed 781 tests: Utilities 329, Parsing 161, Application 280 and Generation 11. The existing opt-in large-file test was skipped. The Release desktop build passed with zero warnings/errors; its DLL reports file version 2.0.20.0. `git diff --check` passed. Live input/results navigation checks with 30 synthetic records are recorded below. Installer/portable packaging and GitHub publication are handled by the tagged CI workflow.

## Reopened log input visibility — 24 November 2026

Replaced the post-parse input area's 30% height cap with a full-workspace input view. A fixed Back to results button restores the existing results, the log editor grows with the workspace, and the editor and input page expose scrollbars. Reopening input returns the page to its top; keyboard focus follows the active view. Parsing still opens results automatically when that setting is enabled.

The desktop Debug build passed with zero warnings/errors. Live checks in the development app parsed 30 synthetic records, reopened input with all text retained and a readable editor, and returned to the same 30 results and selected record using Back to results. The original running app was left open; the separate development test window was closed. Very small windows and alternate text scaling remain manual checks.

## Version 2.0.19 release checks — 24 November 2026

Bumped package/file version to 2.0.19 while retaining SDK assembly identity 2.0.0.0. Message Compare's field picker now lists only top-level parsed fields. Selecting a field includes its descendants when Show subfields is enabled, with request/reply and differences filters still applied. Updated picker guidance, examples and README. Paired comparison cards show a send icon beside Request and a return arrow beside Reply, including subfield cards.

The full Release suite passed 781 tests: Utilities 329, Parsing 161, Application 280 and Generation 11. The existing opt-in large-file test was skipped. Updated comparison tests verify top-level choices, parent selection with subfield visibility, multi-field search and selection preservation. The Release desktop build passed with zero warnings/errors; its DLL reports file version 2.0.19.0. `git diff --check` passed. Live visual checks of the icons remain manual. Installer/portable packaging and GitHub publication are handled by the tagged CI workflow.

## Version 2.0.18 release checks — 24 November 2026

Bumped package/file version to 2.0.18 while retaining SDK assembly identity 2.0.0.0. Text Compare now shows a dot (`.`) in each gutter for wrapped continuation rows, while actual text lines retain their logical line numbers. Gutter rendering locates visible character positions before stepping through display lines, including continuations whose numbered row has scrolled above the viewport. Updated the tool help and README.

The full Release suite passed 781 tests: Utilities 329, Parsing 161, Application 280 and Generation 11. The existing opt-in large-file test was skipped. The Release desktop build passed with zero warnings/errors; its DLL reports file version 2.0.18.0. `git diff --check` passed. Live visual checks of continuation dots, scrolling and resizing remain manual. Installer/portable packaging and GitHub publication are handled by the tagged CI workflow.

## Version 2.0.17 release checks — 24 November 2026

Bumped package/file version to 2.0.17 while retaining SDK assembly identity 2.0.0.0. Message Compare now has Request/Reply filters, a Show subfields checkbox, and Show only differences checked by default. The searchable field picker generates choices from both parsed inputs, includes decoded subfields, accepts comma/semicolon/newline-separated searches, and supports selecting matching fields together. Filter checkboxes share one action row, and comparison cards use tighter spacing, padding and slightly smaller text.

The full Release suite passed 781 tests: Utilities 329, Parsing 161, Application 280 and Generation 11. The existing opt-in large-file test was skipped. The 16 Message Compare tests cover combined filters, parsed field choices, multiple search terms, selections, subfield visibility and stale-result invalidation. The Release desktop build passed with zero warnings/errors; its DLL reports file version 2.0.17.0. `git diff --check` passed. Live visual checks of the updated cards and filter controls remain manual. Installer/portable packaging and GitHub publication are handled by the tagged CI workflow.

## Version 2.0.16 release checks — 24 November 2026

Bumped package/file version to 2.0.16 while retaining SDK assembly identity 2.0.0.0. Includes the ISO 8583 message builder, text/hex field input, automatic bitmap/prefix generation, EBCDIC encoding, accordion field editors and fitted hex/ASCII previews.

The full Release suite passed 777 tests: Utilities 329, Parsing 161, Application 276 and Generation 11. The existing opt-in large-file test was skipped. The Windows Release build to `artifacts/releasebuildercheck` passed with zero warnings/errors. SDK-only developer-kit verification built and packaged all three standalone sample plugins and includes the builder API source. `git diff --check` passed. Live accordion, editing, packed preview and maximized layout checks are recorded below. Installer/portable packaging and GitHub publication are handled by the tagged CI workflow.

## ISO builder accordion and fitted previews — 24 November 2026

Replaced the field cards with native expanders that start collapsed, show a short live value/format summary, and open one editor at a time. Existing editor controls and expansion state survive adding/removing other fields. Replaced bounded preview editors in a stack with equal flexible grid rows inside a fitted result pane; compact viewport sizing is disabled so both editors use the available height. Minimum editor heights preserve a usable scrollable layout in short windows, and the optional packed-field list has a bounded viewport.

The desktop build to `artifacts/isobuilderaccordion` passed with zero warnings/errors. Live checks loaded the ASCII sample, observed MTI and newly added field 2 collapsed, opened field 2, entered `12345`, opened MTI and verified field 2 closed while retaining its header value. Build preview then produced the complete 27-byte ASCII message `020040000000000000000512345` with hex and ASCII outputs. Both previews filled the available pane in the restored window and grew substantially after maximizing. This completes the previously interrupted basic builder-preview UI check. Alternate themes, very small windows and clipboard actions remain manual. No packing or workspace logic changed in this layout update.

## ISO 8583 message builder — 24 November 2026

Added Generators → ISO 8583 Message Builder using the same startup-loaded jPOS XML profiles and optional tab-local XML load as the parser. Fields accept logical ASCII text or encoded hex value bytes without a prefix. The engine generates primary/secondary/tertiary bitmaps, prefixes and fixed-text padding, applies IBM1047/CP500 only to EBCDIC codecs, validates header/field sizes and values, then parses the message back before exposing its hex/escaped-ASCII preview. Variable BCD hex has an explicit digit-count option for odd lengths. Editing, changing profile, removing fields or clearing invalidates the preview. Copy ASCII is restricted to fully printable ASCII messages; binary/EBCDIC output remains available as lossless hex. No framing or MAC calculation is added.

The desktop build to `artifacts/isobuildercheck` passed with zero warnings/errors. Parsing tests passed 161 cases, including 34 new builder cases for known packed bytes, prefixes, mixed text/hex, fixed padding, signed amounts, BCD/F/hex encodings, CP500, binary fields, headers, secondary/tertiary bitmaps, field order, malformed values, duplicate/unknown fields, capacity limits and cancellation. Application tests passed 276 cases with one opt-in large-file test skipped; five new cases cover add/remove, profile/reset behavior, error feedback, digit counts, mode changes and preview invalidation. The live UI check opened the tool, loaded the ASCII sample XML, added field 2 and entered `12345`; repeated user-input detections prevented completing the Build preview click. End-to-end preview/copy interaction and alternate window/theme checks remain manual. `git diff --check` passed.

## Version 2.0.15 release checks — 24 November 2026

Bumped package/file version to 2.0.15 while retaining SDK assembly identity 2.0.0.0. Includes the raw ISO 8583 parser and results tree, jPOS XML/user-folder/plugin profiles, IBM1047/CP500 compatibility, Message Compare request/reply pairs and expanding inputs, and Text Compare line numbers and wrapping.

The full Release regression suite passed 738 tests: Utilities 329, Parsing 127, Application 271 and Generation 11. The existing opt-in large-file test was skipped. The versioned Release desktop build passed with zero warnings/errors in `artifacts/releasecheck`; its DLL reports file version 2.0.15.0. An earlier output directory containing dotted version segments caused a PRI qualifier warning; the neutral directory avoided it without a source change. The SDK-only developer-kit verification passed, building and packaging all three standalone sample plugins. The example package includes both ASCII and EBCDIC ISO XML definitions. `git diff --check` passed. Installer/portable packaging and GitHub publication are delegated to the tagged CI workflow.

## jPOS field packagers and EBCDIC — 24 November 2026

Expanded the explicit codec mappings against the official jPOS field-packager tutorial and concrete class sources. The parser suite passes 127 tests, including 56 new compatibility cases covering ASCII/EBCDIC/BCD prefixes, fixed and L/LL/LLL/LLLL character/binary fields, signed amounts, odd BCD/F padding, packed hex, track-2 separators, malformed lengths, tertiary bitmaps, CP500 versus IBM1047 punctuation, raw offsets and following-field alignment. The application suite passes 271 tests with one opt-in large-file test skipped; the new workspace test verifies CP500 selection, source hex preservation and result invalidation when the code page changes.

The desktop build to `artifacts/iso-ebcdic-check` passed with zero warnings/errors. In the running app, loaded the synthetic `example-ebcdic.xml`, selected Hex bytes and CP500, and parsed MTI `0200`, bitmap `6000000000800000`, account `12345`, processing code `000000`, and terminal `TERM[]! ` from CP500 hex. Results reported complete, and source values remained the original EBCDIC hex, including terminal `E3C5D9D44A5A4F40`. Decoder/length behavior has automated coverage; the live check used dark theme and did not repeat broader theme/DPI, clipboard or keyboard tests. Custom Java/composite packagers and nonstandard root layouts remain explicitly unsupported; see the class list in PLUGINS.md.

## ISO 8583 results tree — 24 November 2026

Replaced field cards with a native TreeView. Each field heading contains its decoded value once; children expose source value and wire details. New results default to level 1. Expand all, Collapse all and Expand level 1 were exercised with a truncated synthetic message, verifying retained MTI, bitmap, account, partial processing code and warning. Expand all exposed the account prefix `05`, full source `0512345`, hex and byte ranges; Collapse all retained only the four field headings; Expand level 1 restored the correct sources with wire details closed. This sequence exposed recycled UI content showing another node's text; the final template binds to node data and passed the same sequence without stale content.

The final desktop build to `artifacts/iso-tree-verified` passed with zero warnings/errors. A separate output directory avoided replacing the running app's locked executable. No parser behavior changed. Alternate themes, narrow windows and keyboard/screen-reader navigation remain manual checks.

## Text Compare line-number alignment — 24 November 2026

Aligned each number's measured TextBlock baseline to the corresponding RichEdit text baseline, including the inner ScrollViewer's padding and position. This removes the vertical offset between line numbers and text while retaining scrolling and logical line numbering when wrapped.

The separate-output Debug desktop build passed with zero warnings/errors, and `git diff --check` passed. Live checks verified alignment at the start of a 105-line input, around lines 100–105 after scrolling to the end, and before/after a long wrapped line. Alternate DPI and Windows text scaling remain manual checks.

## Text Compare line numbers and wrapping — 24 November 2026

Added logical line numbers inside both Text Compare inputs and a Wrap text checkbox controlling both editors. Gutter width grows with the number of digits, visible numbers follow each editor's scroll position, and wrapped continuations do not receive extra numbers. Only visible numbers are drawn, using a binary search over cached line starts. Wrapping leaves text and comparison highlights intact. Updated the help and README.

The final separate-output Debug desktop build passed with zero warnings/errors, all 15 existing text-comparison utility tests passed, and `git diff --check` passed. Live preview checks used 80-line samples in both inputs, verified numbers beyond line 19 through line 80 after caret scrolling, wheel scrolling back through the input, wrapping a long logical line, returning to unwrapped text, preserved red/green comparison highlights and expansion when maximizing. An intermediate build could not replace the running preview's executable; the final build used a new output directory. Alternate DPI/text scaling and narrow stacked panes remain manual checks.

## Message Compare expanding inputs — 24 November 2026

Moved the Message 1 / Message 2 inputs into a fitted grid with equal flexible rows and removed their 240-DIP height cap. The editors now fill the input pane as the window grows, while the spec, guidance and actions keep their natural height. A minimum form height preserves usable editors and outer scrolling in smaller windows. Input headings and validation consistently use Message 1 / Message 2.

The separate-output Debug desktop build passed with zero warnings/errors, and `git diff --check` passed. Live preview checks opened Message Compare, maximized the window and verified both editors expanded to fill the available height, then restored the window and checked that both inputs and the actions remained visible. No comparison logic changed in this layout update.

## Message Compare request and reply pairs — 24 November 2026

Message Compare now accepts one message per box or a request followed by its reply in each box. Matching is positional: request against request, then reply against reply. Paired field/subfield paths carry Request / Reply labels, and the summary identifies single-message or paired comparison. Both boxes must contain the same number of messages, with at most two each. Validation still rejects truncated previews and now checks every parsed message for fields. Updated the input guidance, help and README.

All 12 Message Compare workspace cases passed, covering request-only behavior, paired values and filtering, mismatched counts, extra messages, returning to a single message, Clear, edits during parsing, and decoded reply subfields/missing fields with LF, CRLF and CR boundaries. The full application suite passed 263 tests with the existing opt-in 2-GiB test skipped. The separate-output Debug desktop build passed with zero warnings/errors. The sandboxed test runner could not connect to its test host; the compiled tests passed outside the sandbox. Live UI layout and interaction remain manual checks.

## Version 2.0.14 release checks — 23 November 2026

Bumped the package/file version to 2.0.14 while retaining SDK assembly identity 2.0.0.0. This revision includes Message Compare and Text Compare, required-input feedback, timed-key scheduling changes, the plugin manager with optional private GitLab updates, and distinct neutral tab styling. Updated the README's plugin-management path and update summary.

The versioned desktop Debug build passed in a separate output directory with zero warnings/errors. All four regression projects passed: Utilities 329, Application 257, Parsing 56 and Generation 11 (653 passed total). The existing opt-in 2-GiB application test was skipped. `git diff --check` passed. Installer/portable release packaging and publication are handled by the tagged CI workflow and were not run locally.

## Neutral tab colours — 23 November 2026

Replaced blue tab fills and outlines with neutral graphite/grey in dark mode and soft grey/charcoal in light mode, including hover and pressed states. The two-DIP outlines and spacing remain, and high-contrast mode retains Windows system colours. The separate-output Debug desktop build passed with zero warnings/errors. The preview was launched, but live colour verification was interrupted by user activity in the window; alternate themes remain manual checks for this palette change.

## Distinct tab outlines and spacing — 23 November 2026

Added shared two-DIP tab borders, a two-DIP horizontal margin on each tab, stronger selected outlines and blue selected fills in dark/light themes. Inactive fills remain neutral and high-contrast resources use Windows system colours. A shared attached property places the tab overrides at each item's resource scope so WinUI's template state resources use them; the native tab template, close buttons and keyboard behavior are retained.

The separate-output Debug desktop build passed with zero warnings/errors. Live checks verified visible outlines on outer Dashboard/Settings tabs and inner General/Plugin manager tabs, selected-tab changes, and switching between dark/default and light themes. The original Default theme preference was restored. Alternate DPI, high-contrast mode and tab overflow remain manual checks.

## Private GitLab plugin updates — 23 November 2026

Added per-plugin GitLab token entry, replacement and removal through Windows Credential Locker. Tokens are separate from manifests/settings and scoped to the plugin ID plus feed HTTPS origin. Checks and downloads use PRIVATE-TOKEN only for that origin. Redirects are explicit, limited to five hops and reject HTTP destinations; other HTTPS hosts receive no token. Access failures explain how to check the token and permissions. Removing all local data also removes ProtoBench's stored update credentials. Developer documentation includes private GitLab raw-file API feed/package examples and token scopes.

The separate-output Debug desktop build passed with zero warnings/errors. The application suite passed 257 tests with the existing opt-in 2-GiB case skipped. Eleven new authentication cases cover authenticated checks/downloads, unchanged shared headers, other-host/port package destinations, same-origin/cross-origin redirects, HTTP redirect rejection, redirect limits and access-failure messages without token disclosure. Real GitLab access and Windows credential save/remove UI have not been exercised; no project URL or real token was supplied.

## Plugin manager and online updates — 23 November 2026

Settings now has General and Plugin manager tabs. The manager retains import, enable/disable, installed/session/pending version details and folder access, and adds per-plugin and bulk update checks, release notes, verified downloads and Reload app. Plugins opt in through an optional HTTPS `updateUrl`; unsupported plugins retain manual updates. Downloads validate feed compatibility, SHA-256, package identity/version and existing package safety rules before staging a replacement for startup.

The separate-output Debug desktop build passed with zero warnings/errors. The application suite passed 246 tests with the existing opt-in 2-GiB case skipped. Twenty-three new updater cases cover successful staging and startup activation, disabled-state preservation, installed/pending version comparisons, manual-only plugins, invalid URLs/feed metadata, mismatched checksums/IDs/versions, traversal, size limits with and without a declared length, HTTP failures, cancellation and insecure redirect destinations. Live checks verified Settings tab navigation, the plugin manager layout, empty installation state and the Check all result when no plugin supports online updates. Real developer-hosted feeds, installed-plugin update controls, narrow layouts and alternate themes remain manual checks; network/package behavior was verified using deterministic HTTP test responses.

## Required input highlighting — 23 November 2026

Added shared required-input feedback to Bitwise Calculator, Base Converter numeric mode, payment tools, Message Compare, Log Parser paste/path actions, certificate inspection, fixed-width generator Fields/JSON editors, relay/server configuration, Timed Key Presser and standard plugin forms. Required headers show an asterisk before running. An attempted action marks all missing inputs with a critical border, inline description and accessible help, then focuses an invalid field. Editing values clears corrected errors; Clear resets validation. Conditional requirements follow the active mode. Existing 3DES field feedback is retained. Valid empty text operations and empty bitmap field-list encoding remain supported; file-picker actions and Stop bypass unrelated required inputs. Custom plugin views retain their own validation UI.

The separate-output Debug desktop build passed with zero warnings/errors. Application and generator suites passed 234 tests with the existing opt-in 2-GiB case skipped. A generator test verifies required-input metadata against actual generation and optional-field behavior. Live checks verified missing ARQC key/data feedback, clearing a corrected data field, all three missing Message Compare inputs, resetting errors with Clear, and switching Bitmap from required decode input to optional field-list encoding followed by successful all-zero output. No listeners or scheduled key presses were started during UI verification. Individual plugin views, alternate DPI/themes and every conditional networking combination remain manual checks.

## Tab boundary contrast — 23 November 2026

Added shared TabView theme overrides for visible tab outlines, separators and the tab-strip baseline. Active tabs use a distinct blue-grey surface, with separate inactive, hover and pressed fills in dark and light themes. High-contrast mode retains system state colours with system-colour outlines. These resources cover workspace tabs and nested parser tabs. The separate-output Debug desktop build passed with zero warnings/errors; the new tab colours have not been interactively verified.

## Shared theme border contrast — 23 November 2026

Strengthened shared card/panel outlines, text-input borders and dropdown borders in dark and light themes using opaque neutral slate brushes. Hover outlines have additional contrast, focus accents are retained, and high-contrast mode uses Windows system colours. Dashboard card surfaces and borders now use theme resource expressions so they follow the selected theme. The separate-output Debug desktop build passed with zero warnings/errors. These theme adjustments have not been interactively verified.

## Message Compare value-panel definition — 23 November 2026

Message 1 and Message 2 subcards now have opaque contrasting surfaces, two-DIP outlines, more padding and a divider between the label and value. Dark mode uses a cool charcoal surface against the yellow/purple parent cards; light mode uses white, with explicit high-contrast resources. Difference text highlighting is retained. A separate-output Debug desktop build passed with zero warnings/errors; the normal output was locked by the running app and Visual Studio. This styling adjustment has not been interactively verified.

## Message Compare result readability — 23 November 2026

Replaced the dense comparison rows with separated cards, 16-DIP field headings and monospaced values, explicit 24-DIP value line height, bordered value panels and status badges. Changed cards use a yellow edge and missing-field cards a purple edge, with distinct light/dark/high-contrast resources. Red and green text highlights mark the differing span between the common prefix and suffix, preserving emoji and combining characters. Matching parent values stay unhighlighted when only decoded children differ. Parser diagnostics are available in a collapsed expander above the results.

The final Debug desktop build passed with zero warnings/errors. Live checks verified the compiled template loads, matched values, the differences-only filter, missing-field styling, changed ICC data and text highlighting. The initial live check caught missing runtime XAML type metadata; moving the template into a compiled resource dictionary resolved it. Alternate DPI/theme combinations remain manual.

## Parser message comparison — 23 November 2026

Added Message Compare in Parsers with two paste boxes, an installed-spec picker, recursive field/subfield comparison, side-by-side values and a differences-only filter. Matching uses spec ID/name within each parent plus occurrence for repeated fields. Missing and empty values stay distinct; nested differences mark parent rows changed. Validation requires one complete parsed message per box; editing inputs or changing specs invalidates results. Parser diagnostics are shown above the comparison.

The Debug desktop build passed with zero warnings/errors. The parser and application regression suites passed 279 tests with the existing opt-in 2-GiB test skipped. Twelve new tests cover field ordering and IDs, nested changes, repeats, missing/empty values, exact comparisons, selected-spec parsing, validation and edits during parsing. Live app checks opened Parsers → Message Compare, selected the installed synthetic record spec, compared a changed amount, filtered to the difference, and compared decoded ICC tag 9F02. The last check showed the changed parent 55 and child 55 / 9F02 with both values and parser warnings. Alternate DPI/themes and narrow-window interaction remain manual.

## Timed Key Presser default and random timing — 23 November 2026

Changed the default interval to 200 seconds. Added an unchecked Randomize time between key presses checkbox, locked with the other configuration while running. When enabled, a new random delay from 1 second up to the configured interval is chosen before every press, including the first. The selected key stays unchanged; late ticks still send at most once and schedule from the current time.

Release application tests passed 217 cases with the existing opt-in large-file case skipped. Deterministic timing tests cover the default, resampling delays, unchanged key selection, configuration locking, late ticks and the one-second boundary. The Release desktop build passed with zero warnings/errors. The new checkbox has not been interactively verified.

## Timed Key Presser held-key behavior — 23 November 2026

Removed the held modifier/selected-key guard and skip result. Every due interval now attempts the selected key's down/up pair without querying held keys or changing modifier state. A physically held modifier can still affect how Windows interprets the key. Rejected or incomplete Windows input still stops the session and records the failure. Updated the UI help and README to match.

Release application tests passed 214 cases with the existing opt-in large-file case skipped. The Release desktop build passed with zero warnings/errors. Native input while physically holding keys has not been interactively verified.

## Tagged workflow relay-test timing — 23 November 2026

The v2.0.11 tagged run failed in `TcpRelayTests.AutoModeHandlesEmptyAndSilentClientsAndStopCancelsPeek`, before release packaging. Its shared 200 ms setup timeout expired while the CI runner was processing the empty client's half-close, producing `OperationCanceledException` instead of the EOF event asserted by the test. The same commit's main-branch build passed. All update/proxy application tests passed in the failed tagged run.

Version 2.0.12 separates empty-client EOF, silent-client timeout, and stop-during-peek into three tests. Only the silent-client case uses the short timeout. The stop case waits for an accepted active handler before stopping, and verifies that cancellation closes it without a setup-failure event. Production relay and updater code are unchanged. The full Release suite passed 589 tests with the existing large-file test skipped. The three relay cases also passed ten consecutive targeted runs.

## Update proxy fallback — 23 November 2026

Version 2.0.11 adds a saved Fallback proxy address textbox and Save proxy action under Updates. Blank disables the fallback; HTTP(S) host/port addresses are normalized and validated before persistence. Startup waits for settings to load before checking. Update requests use the normal OS connection first, then retry once through the configured proxy on connection/timeout failures, blocking HTTP statuses or server errors. The same downloader covers portable manifests and installed GitHub API/feed/package traffic. Package retries truncate partial output, and cancellation/local file errors do not trigger another route. HTTPS and Velopack package verification remain unchanged. Browser download-page navigation uses browser settings.

The Release solution build passed with zero warnings/errors. All 587 tests passed with the existing opt-in 2-GiB case skipped. Thirty-one new cases cover address validation, normal-path success, fallback triggers, timeout versus cancellation, blank/changed settings, failed proxies, header/timeout propagation, partial files, portable manifests, saved settings, real loopback HTTP proxy routing, and the GitHub source's API/feed/package path through a simulated proxy. Existing settings JSON tests also verify persistence and the empty default for older files. The native textbox and a real corporate HTTPS proxy/install/restart cycle have not had an interactive smoke check.

## SDK structured tool results — 23 November 2026

Version 2.0.10 adds optional search and field-decoder result contracts to API 2, with shared WinUI host rendering. Search presents ordered hits, excerpts, a selected passage, literal term emphasis, source labels and collapsed ranking/raw details. Decoding presents status, raw value, summary, labeled component/value/meaning rows, expandable children, warnings, context, references and copy actions. Integer and supporting-field inputs, optional input groups, and input-edit invalidation are available to standard portable plugins. Existing text/tree tools and custom WinUI providers remain supported.

Release solution and native UI builds passed with zero warnings/errors. The full suite passed 556 tests with the existing opt-in 2-GiB test skipped. Fifteen new cases cover structured results with empty raw output, clear/edit/disposal invalidation, mixed-presentation rejection, numeric bounds and fractional input, invalid/duplicate supporting fields, exact value preservation, cancellation, and loading/executing the standalone structured-results plugin against shared host contracts.

The complete developer kit (matching app and SDK) was built at `artifacts/developer-kit-2.0.10.zip`. All three standalone sample projects compiled and packaged successfully; SDK/app DLL hashes matched. The kit includes the new contract source, guide and synthetic search/decoder sample. The sample does not implement BM25 or real specification mappings. An isolated native visual-test harness failed to expose a targetable window, so live appearance, narrow-window behavior, keyboard selection and clipboard actions have not been visually verified. No installed plugins or user settings were changed. The HTML design previews remain local and are not release source.

## Manual sidebar expansion — 22 November 2026

The title-bar menu toggle now overrides responsive Auto mode for the current session. Opening it docks the pane; selecting a category/tool and resizing no longer dismiss it. Explicit collapse uses the compact rail or hides the pane at narrow widths. Menu-container replacement is queued after pane transitions instead of mutating NavigationView during its property callback.

Release solution build passed with zero warnings/errors. Live checks confirmed opening from compact mode, selecting Utilities, keeping the pane docked while narrowing through both responsive breakpoints, explicitly collapsing/reopening below 640 DIPs, and opening the relay with the sidebar still visible. The verification copy was closed; the original Debug session was preserved.

## Timed Key Presser — 22 November 2026

Added the Utilities workspace with key selection (F1–F24 and common keys), an interval in seconds, combined Start/Stop, locked configuration and a bounded timestamped auto-scrolling activity textbox. The UI-thread timer continues across tab switches; workspace disposal stops it. Native SendInput emits a down/up pair, checks held modifiers/keys, and reports rejected or incomplete input. Scheduling uses monotonic elapsed time and does not replay overdue presses after a pause.

Release build passed with zero warnings/errors. The suite passed 517 tests with the existing opt-in 2-GiB test skipped. Fifteen new cases use a fake clock and fake keyboard without injecting real input: validation, first-press delay, repeated intervals, input locking, stop/restart/disposal, late ticks, wall-clock changes, held-key skips, failed delivery, catalog validation and log retention.

Live Release checks opened the tool through dashboard search, confirmed F24/300 defaults, changed to one second, observed real accepted F24 presses and timestamps, disabled inputs and the Stop label, then stopped and confirmed inputs unlocked. A second run continued while Dashboard was selected; returning showed the increased count and log scrolling to later entries. The session was stopped with 53 total presses. The verification window was left stopped. Delivery to elevated targets, Windows suspension and alternate DPI/theme configurations remain manual.

## Category dashboard navigation — 22 November 2026

Reproduced opening TCP / TLS Relay and clicking Utilities in the collapsed sidebar: the dashboard returned to All tools and highlighted Home. Dashboard category filtering now keeps its own state across tab unload/reload, restores the dropdown on load, and synchronizes the sidebar with the active category. Selecting a category activates the Dashboard tab before applying its filter; the page heading identifies the section. Explicit Home navigation still resets to All tools.

Verified in a separate Release instance while preserving the running Debug session: Utilities showed exactly TCP / TLS Relay, Test Telnet Server and X.509 Certificate Inspector after returning from the relay tab, with Utilities highlighted; Home showed all 13 tools; Payments showed its four tools. The verification copy was closed afterward.

## Responsive workspaces — 22 November 2026

Added wrapping action rows, compact page spacing, bounded output editors, coordinated navigation breakpoints and an open-tabs menu. Standard narrow tool workspaces switch between Inputs and Results without recreating editors. Parser/relay details use full-width navigation; parser field details no longer force a 560/720-DIP minimum viewport. Generator field editing preserves the JSON representation. Settings and dashboard layouts were compacted, with per-tool result improvements for calculators, payments and certificates.

The Debug solution builds with zero warnings/errors. The four test projects pass 502 tests with the existing opt-in 2-GiB test skipped. The initial sandboxed test run could not connect to testhost; running the same command outside that sandbox completed successfully. Parser presentation tests now verify that opening details preserves the existing viewport budget.

Live checks covered dashboard reflow in narrow/short windows, keyboard search/Enter in the new-tab picker, bitwise calculation and automatic result navigation, generator field population and a 16-byte preview, relay configuration access and field validation, grouped Settings, and EMV example loading/byte selection across narrow and wide layouts. The EMV check caught a visual-parent exception; byte editors now remain in one container and switch visibility. Repeating the check preserved E0F8C8 and Byte 2/F8 through both resize directions without crashing. No listeners were started or files exported during these checks.

Broader verification remains manual: enlarged Windows text and alternate DPI/theme combinations, parser navigation with installed format definitions, relay inspection with active traffic, custom plugin views, and native file dialogs. Recommended window sizes are 1280×800, 1024×768, 800×600 and a narrow snapped window. Check primary actions, keyboard focus, Back navigation, retained input/selection, and long field/error text at each size.

## Relay payload tab alignment — 16 November 2026

Moved the capture summary into the left traffic pane so the right Hex/Text/Details tabs start at the top of the shared card in the side-by-side layout. Summary text can use four lines within the narrower traffic pane. The existing narrow-window stacking and footer actions are retained. The tab alignment was visually checked in the running app; the Desktop build passed with zero warnings/errors.

## Independent upstream trust and validity-date checks — 16 November 2026

Added Verify upstream certificate trust and Verify upstream validity dates alongside hostname verification, all enabled by default. Unknown-CA/partial-chain and date errors can be relaxed independently through chain-policy flags; other certificate/TLS errors remain enforced. Connection events record all three settings. Nine real-socket test cases cover trusted/untrusted, expired/not-yet-valid and wrong-name combinations, including successful traffic when the relevant checks are disabled and rejection when an unrelated enabled check fails. The full suite passed 425 tests with one existing opt-in test skipped; the Desktop build passed with zero warnings/errors.

## Relay upstream matching and hostname control

Added Upstream connection choices Plain TCP, TLS and Match incoming, plus Verify upstream hostname (default on). Matching follows each incoming connection's actual transport; upstream SNI uses an explicit override, then client SNI in Match mode, then the configured upstream host. Endpoint routing remains fixed and TLS versions/ciphers negotiate independently. Real-socket tests cover mixed transports, client-SNI reuse, missing-SNI fallback and an override with a different upstream certificate name. Disabling hostname verification allows a name mismatch with a trusted valid certificate while still rejecting untrusted and expired certificates. The full suite passed 416 tests with one existing opt-in test skipped; Desktop builds passed with zero warnings/errors. The new upstream dropdown, Match incoming selection and checked-by-default hostname control were visually verified in the running app.

## Relay viewport, automatic TLS and hostname diagnostics — 15 November 2026

Replaced the relay's scrolling page with bounded grid rows and independent traffic/payload panes, enabled stretching in the shell and payload TabViews, compacted the toolbar and added height-aware connection-panel collapse. The layout was visually checked at the normal 1320×900 startup size and in a temporary 840×640 QA build; traffic and the hex/text inspector stayed in the same viewport, with connection settings collapsed in the short window. The QA-only startup-size change was reverted. Incoming Auto mode peeks without consuming the first byte, selecting TLS for `0x16` and plain TCP otherwise, and generates a test certificate if needed. Upstream certificate errors now report the checked name, subject/SAN and SNI/trust guidance while retaining strict validation. The full suite passed 409 tests with one existing opt-in 2 GiB test skipped, including mixed plain/TLS clients on one port, preserved first bytes, empty/silent clients, stop during peek, automatic certificate creation and useful name-mismatch diagnostics. Desktop builds passed with zero warnings/errors.

## Relay layout — 15 November 2026

The relay now places the full-width Connection section above the full-width Traffic section. Listen and upstream settings sit beside each other within Connection and stack on narrower windows using the existing adaptive layout. The revised layout was visually checked in the running app; the Desktop build passed with zero warnings/errors.

## TCP / TLS relay — 15 November 2026

Added a Utilities workspace with independent incoming/upstream TLS, PFX/P12 loading, test-certificate creation and public certificate export, upstream trust/name validation, a bounded traffic ring, hex/UTF-8 inspection and JSON Lines export. Real loopback integration tests cover all four plain/TLS leg combinations with binary payloads, concurrent clients, plain TCP half-close, trust/name rejection, connection limits, setup timeouts, shutdown/port release, capture eviction and workspace/PFX/export lifecycle. The full suite passed 406 tests with one existing opt-in 2 GiB test skipped. Desktop builds completed with zero warnings/errors. Windows UI automation opened the workspace, started/stopped it, confirmed configuration locking, relayed `Hello` and received `ACK:Hello`, observed both capture directions and byte counts, and checked `41434B3A48656C6C6F` / `ACK:Hello` in the hex/text inspectors. Test-certificate generation displayed its SHA-256 fingerprint and expiry. No machine trust or firewall settings were changed. The file-picker export dialogs and remote/LAN clients have not been exercised interactively.

## Collapsed sidebar category selection — 15 November 2026

The collapsed navigation rail uses direct category entries so a category click filters the dashboard without opening a child flyout. The expanded sidebar retains its nested tool entries. Windows UI automation reproduced the original problem: with a child flyout open, the first click on another category only dismissed it. In the revised running build, clicking Converters and then Calculators once changed the dashboard from one converter to three calculators. Expanding the sidebar and Converters restored the Base Converter child entry. The Desktop build completed with zero warnings and errors.

## Adjustable field-details width — 8 November 2026

Added a 12px draggable native Thumb divider between field list and details, a horizontal resize cursor and hover/pressed grip feedback. Left/Right keyboard controls adjust by 16px; Home restores the automatic default. Width is retained per parser view across field/message changes and narrow-window stacking, and clamped to keep at least 180px for fields and 220px for details. The divider is hidden for stacked or closed details. The normal Debug solution builds with zero warnings/errors. Dragging, keyboard resizing and focus appearance still require an interactive smoke check; no backend behavior changed.

## Compact message cards and field side panel — 8 November 2026

Messages now occupy a 240px column beside the record inspector. Each selectable card groups record/direction, MTI/role, time, STAN/sequence, terminal, acceptor and a check summary. Search remains immediately available, while filter/sort controls are in an expander. Validation uses red violations, yellow warnings/unresolved checks and green no-findings indicators, with descriptive text/tooltips and a legend. Palette variants support light/dark themes and foreground text replaces status colors in high contrast. Selecting a field opens a closable details panel beside the field list; narrow layouts stack panels. Copy, source navigation, full values and decoded trees remain available.

The Release preview compiled successfully and was inspected in the running app at approximately 1043 × 714. The four-message sample rendered as compact cards beside the inspector with green/yellow status dots. Selecting a field opened its right-side details panel with Copy/View source actions; the field list remained visible. The final normal Debug solution builds with zero warnings/errors. Final wording, empty-details spacing, high-contrast handling and narrow-layout corrections compile but have not had a separate interactive recheck. Light/high-contrast appearance, resizing and red-violation rendering remain manual checks. Parsing rules are unchanged.

## Message browsing and inspector usability — 8 November 2026

Replaced the two cramped side-by-side panels with a full-width message table and an inspector below it. Column headings sort; selection persists across sorting/filtering when the record remains visible. Previous/Next buttons complement keyboard row navigation. The input area collapses after successful parsing. Inspector fields use separate DE/name/value/status columns, with missing required fields represented as rows. Selecting a field opens its complete value, copy/source actions and decoded children; source offsets are moved to decoded-node tooltips. Related-message candidates are clickable and clear conflicting filters when necessary. Required errors are distinguished from unresolved checks. Accessibility row text describes content rather than exposing managed type names.

A separate Release preview build was exercised with the four-message fixture. All four messages rendered in aligned columns; selecting 18223 updated the inspector to 0200 Request. Its field table displayed names and values, and selecting DE3 exposed `001000` with Purchase/Savings decoded children and copy/source controls. The final title-spacing, accessible row descriptions, source-newline conversion and bring-into-view adjustments compile in the normal Debug solution build with zero warnings/errors. Those final small adjustments and the new copy/source/related-navigation actions have not had a separate end-to-end UI recheck. Backend parsing and validation rules are unchanged.

## Term paste boundaries and field tree repair — 8 November 2026

Fixed the header matcher to accept CR-only (WinUI editor), LF and CRLF boundaries without normalizing source text. Logical line numbers count each form correctly. Synthetic message regressions now check MTIs, record IDs, line numbers, raw reconstruction, field byte offsets and ICC entries across all three newline styles. All 26 parser tests pass.

Added an explicit TreeView item template binding each node's Content rather than allowing the control to render the TreeViewNode type name. A separate Release preview build succeeded with zero warnings/errors. Using the running desktop preview, the four-message fixture was pasted into the Log text editor and parsed with automatic detection. The UI displayed **4 of 4 loaded records**, MTIs 0800/0810/0200/0210, and actual field labels/values (Message type, Primary bitmap, Processing code, STAN, etc.) in the inspector. This verifies the repaired paste and field-display path that was previously only covered by library tests. Nested ICC expansion, field-source selection and additional layouts remain candidates for manual checks.

## Configured Term log analysis and pasted input — 8 November 2026

Release solution build passes with zero warnings/errors. All 264 tests pass (Utilities 211, Application 18, Parsing 25, Generation 10). New coverage exercises multiline message boundaries and metadata, CRLF/BOM/UTF-8 source offsets, configurable aliases, padded values, wrapped ICC entries and decoder summaries, malformed/duplicate/unknown fields, missing/forbidden/undetermined requirements, applicability-only conditions, search/filter/sort, ambiguous request/reply candidates, and paste/file transitions with recent-file isolation. UI now offers Paste text and Open file tabs, search/filter/sort controls, validation and related-message inspectors. The desktop build is verified; the new interaction/layout has not been manually smoke-tested. See `TERM-LOG-PARSER.md` for scope, remaining spec gaps, and bounded-preview limits. This supersedes the older statement below that no real NonStop format has been implemented; the new Term profile is an initial implementation, not a complete wire-protocol validator.

## Feature-flagged updates (8 November 2026)

Update checks, download-page fallback and Velopack download/restart support are implemented with `UpdatesEnabled=false` by default. Release builds pass with zero warnings/errors and all 217 tests pass. Ten new update tests cover disabled service guards with no HTTP/backend access, numeric semantic-version ordering, equal/older versions, malformed/prerelease manifests, HTTPS configuration validation, installed versus ordinary folder-copy routing, failed downloads, and explicit restart prerequisites. An enabled build without endpoint properties is rejected by the configuration validation target as expected. The default Release executable started successfully in a process smoke check and was then stopped. Settings controls, browser launching and a real two-version Velopack upgrade still need manual release verification. See `UPDATES.md` for activation and release preparation.

Environment: Windows x64, .NET SDK 10.0.302, Windows SDK 10.0.26100. Validation performed 7 November 2026 (Pacific/Auckland).

## Automated checks

```powershell
dotnet restore ProtoBench.slnx
dotnet build ProtoBench.slnx --no-restore
dotnet test ProtoBench.slnx --no-build --no-restore
```

The full Debug solution build passes with zero warnings and zero errors. Following the ASCII/hex and CP500 additions on 8 November 2026, all 95 xUnit tests pass across four projects:

| Project | Tests |
| --- | ---: |
| Utilities | 65 |
| Parsing | 15 |
| Generation | 10 |
| Application / infrastructure integration | 5 |

Fixture-based parsing reads the shipped synthetic files. Generation compares exact bytes against the shipped binary fixture, including padding and CRLF. Additional checks exercise ambiguous detection, invalid rules and encoded data, transformed field source ranges, BOM/CRLF offsets, record/byte/line limits, cancellation, duplicate IDs, arbitrary-precision numeric conversion, settings persistence, workspace disposal, and preservation of existing files when a staged write fails.

ASCII/hex tests verify exact bytes, mixed-case and whitespace-separated hex input, whitespace/control preservation, the complete 128-character ASCII range, empty input, invalid digits/pairs, non-ASCII rejection and size limits. The new conversion selector compiled successfully; its interactive behavior has not yet had a separate desktop smoke check.

CP500 tests compare decoding of all 256 byte values against `samples/cp500-codepoints.txt`, an independent fixture generated with Python's standard-library `cp500` codec, and verify exact encoding back to the original bytes. Known vectors cover letters, numbers, CP500-specific punctuation, extended characters and controls. Additional tests cover ASCII byte transcoding, invalid hex, size limits, and rejection of unrepresentable text (including the Euro symbol and malformed surrogates). The CP500 selector entries and DI wiring compile; a separate desktop smoke check of those entries remains outstanding.

## Desktop smoke checks

The actual unpackaged WinUI executable was launched and inspected using Windows computer-use tools:

- Dashboard and categorized navigation render.
- Converter accepts the default decimal `255` and produces hexadecimal `FF`.
- Converter and generator tabs coexist; generated preview contains 16 bytes, including three spaces after `ALPHA`, zero padding for `42`, and final `0D 0A`.
- Parser workspace opens and remains stable after its templated controls were moved into XAML.
- A local path to `samples/synthetic-delimited.txt` parses with automatic detection, yielding two records (`hello`/42 and `café`/7), raw text and field source ranges. Status reaches Completed.
- The native file picker opens. Automated selection within PickerHost was not completed because the computer-use helper could not bind that separate host window; the path input completed the end-to-end parser check.

The application must run in the normal interactive Windows session; sandboxed launches exited before presenting a window. Builds and library tests ran in the workspace. Two desktop output paths are possible depending on whether a project or the solution is built; the documented run script uses the explicit x64 path.

## Remaining manual checks

### EMV tag/profile expansion — 8 November 2026

The EMV catalogue now contains 13 tags and 19 explicit tag/profile combinations; coverage and version limits are recorded in `EMV-PROFILES.md`. This adds coded-field summaries, Visa FFI v1, Mastercard Third Party Data, American Express Kernel 4 v2.10 variants, Discover Kernel 6 v2.7 TTQ, CVM List/Results, CID, Terminal Type, AUC and Additional Terminal Capabilities. Profile-aware byte editing preserves raw values and does not infer unsupported version meanings. Common-only TLV decoding leaves ambiguous scheme-specific tags raw; an explicit context selects only matching supported profiles.

The full Release solution builds with zero warnings/errors. All 251 tests pass (Utilities 211, Application 15, Parsing 15, Generation 10). The 34 new utility cases cover catalogue uniqueness, exact bit positions, Visa form factors and unknown versions, Mastercard ASCII device codes/optional device structure, Amex/Discover differences, CVM rule thresholds/order, CID/terminal codes, invalid profile/length combinations, bit round trips across all editable profiles, and explicit versus ambiguous TLV contexts. Sandboxed test runs could not connect to their local testhost processes; the suite completed successfully outside the sandbox.

The referenced site's Visa and Mastercard demo workflows were inspected. In the running preview, the expanded tag list and three distinct 9F6E profiles appeared. Switching from Visa to Mastercard and loading its example produced `02800000323100`, country code `0280`, identifier `0000`, device `3231 (21)` / Mobile phone, proprietary data `00`, and seven correctly sized byte tabs. The layout was visually inspected. Additional profiles, profile-switch errors, byte edits after scheme changes, narrow-window layout and clipboard remain candidates for broader UI testing; the service cases above cover the underlying interpretations.

### Tool layout revision — 8 November 2026

All ten tool workspaces were updated through shared responsive panels: compact input/result columns, related option rows, collapsed help, bounded text editors, and collapsed empty messages/inactive progress. EMV has a full-width byte editor with responsive bit columns and expandable decoded text. Generator text/hex previews and parser field/source inspectors use tabs. The final full Release solution build passes with zero warnings/errors, and all 207 existing tests pass; this change does not alter calculation services.

Running previews were inspected at approximately 1043 × 714 pixels. The 3DES input controls, KCV and results fit together without scrolling. EMV input/operation controls, byte tabs and two-column bit controls were visually inspected with `E0F8C8`; a final compacting pass moved the hex summary beside its actions and removed redundant live-decode controls/status. That final spacing-only pass compiled in the full Release build. Generator Preview still produced the expected 16 bytes. Parser sample loading produced two records and populated the selected record's field tree in the adjacent inspector. The native resize attempt did not change the captured window dimensions, so actual narrow-window stacking, alternate DPI and high-contrast checks remain manual. The responsive grid selects column count from available width and keeps existing controls/state when reflowing.

### Calculator section — 8 November 2026

3DES now shows a separate six-hex-digit encrypt-zero KCV, with Calculate KCV and Copy KCV controls. Successful data calculations populate it too, and editing/clearing the key invalidates it. All 207 tests pass (Utilities 177), including the published `0123456789ABCDEFFEDCBA9876543210` → `08D7B4` KCV, equivalent K1/K2/K1 expansion, parity-bit invariance and invalid-key rejection. Existing running Debug/Release processes locked their output folders, so the current desktop was built successfully to `artifacts/kcv-preview` with zero warnings/errors. In that running preview, Calculate KCV produced `08D7B4` with transaction input empty, and clearing the key cleared the KCV. The ordinary Debug desktop build was blocked by file locks; test assemblies built and passed. Copy KCV and the combined data-result/KCV flow still need separate UI checks.

The Release solution builds with zero warnings/errors and all 205 tests pass (Utilities 175, Parsing 15, Generation 10, Application 5). The 52 new cases cover bitwise operations/bases, width masking and 64-bit boundaries, invalid values and shift counts; RFC 4648 Base64 vectors, UTF-8/binary data, URL-safe alphabets, canonical padding/pad bits and limits; and independent three-key/two-key 3DES vectors, CBC chaining, padding and invalid keys/IVs/lengths. The two-key vectors exposed a Windows one-shot provider restriction with 16-byte keys; the service now expands those to K1/K2/K1 explicitly and the known-answer tests pass.

In the running Release WinUI app, the Calculators category and all three tools were observed. Bitwise `F0 AND 0F` produced `00`; switching to XOR produced `FF` / `255` / `11111111`. Base64 encoded `Hello` to `SGVsbG8=`. The public two-key 3DES vector encrypted `21E81B7ADE88A259` to `5C577D4D9B20C0F8`. Switching ECB to CBC exposed the IV input and cleared the stale result. The Clear inputs smoke check was interrupted by concurrent user input before completion. Bitwise layout was visually inspected in dark theme. The three tool tabs coexisted. Broader alternate-mode, clipboard, theme/DPI and accessibility smoke checks remain manual; service behavior has automated coverage.

### Payment card tools — 8 November 2026

Byte-tab visibility was improved with outlined headers, 14px labels, bold selected text, a 3px accent underline and an outlined editor frame. Brushes use theme resources. The new appearance and switching the active highlight from Byte 1 to Byte 2 were visually verified in the running Debug app in dark theme. Explicit accessible tab names preserve the byte/hex labels with custom header visuals. Light/high-contrast appearance remains uninspected.

The interactive editor now uses embedded Byte 1 / Byte 2 / … tabs, with each byte's current hex in its tab label. The Release build passes with zero warnings/errors and all 153 tests pass. In the running Release app, `E0F8C8` populated tabs E0/F8/C8. Selecting Byte 3 displayed its eight flags; unchecking SDA immediately changed the full input to `E0F848`, the tab label to `Byte 3 · 48`, and byte binary to `01001000`, while retaining the Byte 3 selection. This completes an interactive toggle check as well as the byte-tab switch check.

Interactive EMV bit editing was subsequently added for 9F33, TVR, TSI and AIP. The Debug solution builds with zero warnings/errors and all 153 tests pass (Utilities 123). Five new cases verify editing every bit across all four tags, preserving other bits, reversible changes, idempotent setting, known hex masks and invalid positions/lengths. In the running Debug app, entering `E0F8C8` was observed to populate the interactive checkboxes, live decoded result, full hex and byte-1 binary `11100000`. The checkbox layout was visually inspected. The final click smoke check could not complete because the Windows helper repeatedly reported “window bounds changed” after refreshing; click-driven synchronization, reset, mode switching and clipboard still need a complete interactive recheck.

The Release solution builds with zero warnings/errors. All 148 tests pass: Utilities 118, Parsing 15, Generation 10, Application 5. The 53 added payment cases cover Luhn known check digits and invalid input, primary/secondary bitmap positions and continuation consistency, EMV bit positions and reserved bits, nested TLV offsets/duplicates/long lengths, malformed and bounded TLV parsing, ARQC input validation and verification, an independent published PyEMV ARQC vector, and the ANSI X9.19 method-1 retail MAC known-answer vector. Aligned padding-method behavior is also checked. Contact EMV bit definitions follow EMV 4.3 Book 3 Annex C and Book 4 Annex A2; contactless/later-profile meanings are not inferred.

The actual Release WinUI executable was launched and smoke-tested with Windows computer-use tools. The Payments category and all four dashboard cards appear. Independent EMV, Luhn, bitmap and ARQC tabs opened successfully. Terminal Capabilities `E0F8C8` produced its expected bit descriptions; complete number `79927398713` passed Luhn; bitmap `3020000000800000` reported fields 3, 4, 11 and 41; and the public session-key ARQC vector produced `FA624250B008B59A` with “Verification: match.” The key was masked in the accessibility/UI state. Clear inputs removed key, transaction data, expected MAC and result. The EMV workspace was visually inspected in dark theme. Broader DPI/accessibility, clipboard, alternate modes and theme smoke checks remain manual; the library tests cover their underlying calculations.

### Dashboard and navigation revision — 8 November 2026

The revised dashboard was visually inspected in the running WinUI app. It renders a narrower sidebar, two columns of cards at the inspected window size, local search and category filters, and a persistent Dashboard tab. Opening Base Converter, returning to Dashboard, and using New tab created a labelled Base Converter (2) alongside the existing tab. Search was exercised against the CP500 description. The sidebar uses NavigationView's built-in scrolling menu and adaptive layout; a many-tool overflow scenario and alternate window widths still need broader manual verification.

### New-tab picker and integrated title bar — 8 November 2026

Pressing **+** from Dashboard was observed to open a distinct New tab picker with search focused and the available tools listed. Selecting Base Converter created a workspace and removed the picker. Testing exposed a TabView selection reset during picker removal; the implementation now explicitly reselects the new workspace after removing the picker. That final reselection change compiled and passed the existing suite; its final interactive recheck was interrupted by changing desktop input/window geometry and remains to be confirmed.

The integrated title bar was visually verified with matching dark and light caption backgrounds and contrasting window-control glyphs. Light-theme verification was followed by restoring Default appearance. Native caption buttons are retained. Window dragging, snapping, maximize/restore, and inactive/OS theme changes still need broader manual checks. The full solution builds with zero warnings/errors, and all 95 library tests pass; those tests do not cover native title-bar or tab input behavior.

### Converter space-removal option — 8 November 2026

A per-workspace, initially unchecked checkbox removes ordinary input spaces before any conversion, retaining the original text and leaving tabs, line breaks and output untouched. Toggling it invalidates the previous result. The full Release build passes with zero warnings/errors and all 95 existing tests pass. The running Debug instance locked its executable, so its output could not be replaced; Release verification left that session open. This checkbox has not had a separate interactive smoke check, and existing library tests do not exercise its UI binding.

Clipboard integration, native Save dialog/overwrite flow, theme switching, favorite/recent persistence across app restarts, cancellation and tab closing during a large live operation, accessibility at alternate DPI scales, installer/signing, and distribution to a clean Windows machine still need broader manual/release verification. Services underlying file replacement, persistence, validation, cancellation and disposal have automated coverage. No real NonStop format has been implemented or verified.
