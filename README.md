# RPGMod
https://github.com/ghasttear1/RPGMod

## Modify
- PluginInfo-> 修改为 ```BepInPlugin("com.bamboo98.rpgmod", "RPGMod", "1.3.0")```
- Quest-> 可定制翻译
- Drop-> 新增掉落列表控制,支持ban物品,让物品不再出现在RPGMod的额外掉落中(不影响商店刷新和箱子掉落) 
- Drop-> 新增幸运值是否影响掉落率的设定(始终不影响品质)
- Drop-> 新增BOSS掉落物控制,以及BOSS掉落物是否随机掉落的选项
- Drop-> 新增升级奖励,可以设定在等级提升时是否随机掉落一件1级物品(白色)
- Drop-> 新增掉落时间间隔,可以设定在2次怪物掉落之间的最小时间间隔,有效防止后期大量刷怪时出现大量掉落物
- Game-> 新增每回合开始时额外金钱奖励,可以设置为小箱子的金钱倍数+固定值,整合PocketMoneyMod
- Game-> 新增怪物倍数(虚拟的人数倍数)设置,整合MultiplierMod
- Director-> 新增指令```rpg_show_spawns```可以显示当前场景中可以生成的所有可互动物品的ID
- Director-> 新增指令```rpg_set_multiplier n```设置当前关卡怪物倍数为n
- Director-> 新增指令```rpg_get_multiplier```显示当前设置的怪物倍数
## Modify(English)
- PluginInfo-> modified to ```BepInPlugin("com.bamboo98.rpgmod", "RPGMod", "1.3.0")```
- Quest-> Customizable translation
- Drop-> Added drop list control to support ban items so that items no longer appear in RPGMod's extra drops (does not affect store refresh and box drop)
- Drop-> Whether the added lucky value affects the drop rate setting (always does not affect the quality)
- Drop-> Added BOSS drop control and options for whether or not BOSS drops are randomly dropped
- Drop-> Added upgrade bonus, which can be set to randomly drop a level 1 item (white) when the level is raised.
- Drop-> Add a drop interval, which can be set to the minimum time interval between 2 monster drops, effectively preventing a large amount of falling objects when a large number of monsters are wiped out later.
- Game-> Add extra money bonus at the beginning of each round, can be set to small box of money multiple + fixed value, integrated with PocketMoneyMod
- Game-> Add monster multiplier (virtual multiples) setting, integrated with MultiplierMod
- Director-> Add command ```rpg_show_spawns``` to display the ID of all interactive items that can be generated in the current scene.
- Director-> Add command ```rpg_set_multiplier n``` to set the current level monster multiplier to n
- Director-> Add command ```rpg_get_multiplier``` to display the currently set monster multiplier