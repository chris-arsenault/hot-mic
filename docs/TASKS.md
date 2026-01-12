1
add a plugin for impulse based reverb. it should allow the user to load impulses in common impulse formats. it should ship with some default ones if you can find ones to download. it should have standard reverb controls.
2
UI for AI filters: overhaul the UI for the three AI plugins, the silvero VAD, RNNoise and DFN plugins. they should use modern ux practices and have an overlay that shows what is happening to the audio signal during processing. do research online for common UI controls for these type of AI enabled plugins. do not modify the implementation of the plugin, just the UI.
3
review each of the plugins. choose 2 settings that make sense to elevate to the main channel strip. add these to the main strip with no fancy overlay/ui - just knobs/buttons/values. then, add a midi integration with a common midi livrary (see example in ../slipstream). allow binding of midi CC knobs to paramtes that are on channel strip. implement a quick learn mapping (right click on paramter, enter learn mode, tweak cc, detect changed cc, bind change cc to paramter).add midi settings to the main settings screen (control surface binding, channel, etc).
4
checkbox for loading vst / not in settings  screen. when unchecked VSTs do not load in the plugin interface. additionaly, add catagories for the built in plugins and improve the ux of the plugin selection window