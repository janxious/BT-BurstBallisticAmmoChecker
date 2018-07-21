# BT-BurstBallisticAmmoChecker

In the vanilla game if you try and fire a machine gun (or other burst ballistic effect weapon) and you will shoot more times than you have bullets remaining in your ammo bin, the game can soft lock very easily. This patches the calculations involved so the soft lock is avoided.

## Warning

This will not play nicely with other mods patching `BurstBallisticEffect.Update` due to the nature of the patch, which uses a transpiler to gut that method and offload it to another static method.

## Settings

Setting | Type | Default | Description
--- | --- | --- | ---
`debug` | `bool` | `false` | enable mod-specific logging

## LICENSE

[MIT](LICENSE)
