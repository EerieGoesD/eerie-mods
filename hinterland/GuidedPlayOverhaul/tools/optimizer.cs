// Hinterland Guided Play Overhaul. F8 shows or hides it.
//
// A compass and step-by-step guide for all 9 achievements, using the game's own rules
// decoded from its scripts:
//   fights       server/scripts/combatFormula.cs   CalcCombat ($gCombatMethod 0)
//   leveling     data/Creatures/CreatureFunctions.cs  Creature::AddLevel, CreateCreature
//   monsters     data/Encounters/Encounters.cs   BasicEncounter::onPlaceInitialSpawn
//   achievements server/scripts/highScores.cs, CreatureData::onDisabled, ShowLevelButton
//
// Fight math: each swing rolls one d101 (0-100) per attack point and per defense point,
// so damage and absorb are binomial. Expected damage per swing is summed exactly over
// both distributions, crits included. A fight is scored by expected damage you take
// when you kill enemies in the order that minimizes it (highest enemy damage-per-second
// divided by time-to-kill first).

$Opt::Visible = 1;
$Opt::Refresh = 2000;
// arrow and distance redraw this often (ms); the plan itself every $Opt::Refresh
$Opt::Fast = 150;
// the plan is worked out in small pieces, one piece every $Opt::SliceGap ms, each piece
// stopping after $Opt::SliceBudget ms, so no single frame carries the whole calculation.
// 6 ms (was 3, the player: "decrease the calculation times while keeping the accuracy"): the
// gear totals after a weapon change took 127 s of play at 3 ms
$Opt::SliceGap = 30;
$Opt::SliceBudget = 6;
// what the window shows now, so unchanged parts are not laid out again
$Opt::ShownIdx = -2;
$Opt::Top = 110;
$Opt::Right = 8;
// window spot, saved once you drag it: distance from the right screen edge, and top
// text size, changed with the slider above the window and saved in prefs
if ($pref::Opt::Font $= "") { $pref::Opt::Font = 18; }
// F8 cycles: 0 = one step only, 1 = step + details, 2 = hidden
if ($pref::Opt::Mode $= "") { $pref::Opt::Mode = 0; }
$Opt::Visible = ($pref::Opt::Mode != 2);

// Average enemy gear by creature level (weapon attack / armor defense / shield defense),
// from every weapon, body armor and shield in data/Items within level-3..level+1,
// the window Creature::EquipAsEnemy picks from. Used only before a site is scouted.
$Opt::GearTable = "1.36/0.15/0 1.36/0.15/0 1.38/0.15/1 1.45/0.21/1.5 1.75/1/1.5 2.1/1/1.5 2.1/1/1.5 2.93/1.33/2 3.36/1.5/2 3.9/2/2";
$Opt::GearTable = $Opt::GearTable SPC "4/2/2 4.4/2.5/2 5/3/2 5/3/3.5 4.92/2.67/3.5 6.18/3.4/3.67 6.8/3.5/3.67 6.88/3.5/3.67 6.88/3.5/4 8/4/4";
$Opt::GearTable = $Opt::GearTable SPC "8/3.5/4 8.86/3.5/4 9.4/3.5/4 10/3.5/4 8.88/7/6 8.88/7/6 8.14/7/6 8.14/7/6 6.8/7/6 8.67/6/6";
$Opt::GearTable = $Opt::GearTable SPC "8.67/6/6 8.67/6/6 8.67/6/6 8.67/6/6 12.5/7/7 12.5/7/7 12.5/7/7 12.5/7/7 12.5/7/7 9/9.75/7";
$Opt::GearTable = $Opt::GearTable SPC "9/9.75/7 9/9.75/7 9/9.75/7 9/9.75/7 11.25/7.33/7";

$Opt::Gold = "<color:FFD966>";
$Opt::White = "<color:E8E8E8>";
$Opt::Grey = "<color:9A9A9A>";
$Opt::Green = "<color:7CFC7C>";
$Opt::Yellow = "<color:E6E66A>";
$Opt::Orange = "<color:F0A040>";
$Opt::Red = "<color:F06060>";

// ------------------------------------------------------------------ hooks

// dialogs pause the game, which stops timed refreshes, so refresh when they open
package OptHooksPackage
{
   function LevelLord()
   {
      Parent::LevelLord();
      OptRefreshNow();
   }

   function showNewAbilityDialog()
   {
      Parent::showNewAbilityDialog();
      OptRefreshNow();
   }

   // only a window that really opened or closed: the hire and townsperson windows call this
   // again every half second while they are open
   function setDialogVisible(%show, %dialog, %pause)
   {
      %was = isObject(%dialog) ? %dialog.isVisible() : 0;
      Parent::setDialogVisible(%show, %dialog, %pause);
      %is = isObject(%dialog) ? %dialog.isVisible() : 0;
      if (%was != %is) { OptRefreshNow(); }
   }

   // these two windows open first and only then set who they are about (.talker), so the guide
   // answers again once that is known: no wait for the next timed refresh
   function showHireCandidateDlg(%show, %candidate, %reopenRoster)
   {
      Parent::showHireCandidateDlg(%show, %candidate, %reopenRoster);
      OptTalkerSeen("hire", %show ? HireCandidateDlg.talker : "");
   }

   function showTownsPersonTalkDlg(%show, %follower, %reopenRoster)
   {
      Parent::showTownsPersonTalkDlg(%show, %follower, %reopenRoster);
      OptTalkerSeen("talk", %show ? TownsPersonTalkDlg.talker : "");
   }

   // the town map (T, and the lot picking of a hire) opens zoomed out to the whole town
   // (OptTownZoomApply): its distance is set before the game's own call takes it
   function PlayerTown::enterTownView(%this)
   {
      OptTownZoomApply(OptTownZoom(), 0);
      Parent::enterTownView(%this);
   }

   // closing Options saves moveMap to config.cs: the game's own WASD go into it, never ours
   function OptionsDlg::onSleep(%this)
   {
      %on = $pref::Opt::ScreenWasd;
      %zm = $pref::Opt::WheelZoom;
      if (%on) { OptWasdBind(0); }
      OptArrowBind(0);
      if (%zm && isObject(moveMap)) { moveMap.unbind(mouse, zaxis); }
      Parent::onSleep(%this);
      if (%on) { OptWasdBind(1); }
      OptArrowBind(1);
      if (%zm && isObject(moveMap)) { moveMap.bind(mouse, zaxis, OptWheel); }
   }

   // the game goes on after a window that paused it (an ability picked, a chest closed, a
   // request answered): the next step shows at once. some of those windows are hidden with a
   // plain setVisible(false) and no other call the guide could catch
   function unpauseGame(%userExplicitUnpause)
   {
      Parent::unpauseGame(%userExplicitUnpause);
      OptRefreshNow();
   }

   // a potion drunk with keys 1-4 (customBindings.cs): the guide answers in the same frame, so
   // you see at once whether to drink another or go on
   function UseHealingPotion(%PartyMemberIndex)
   {
      Parent::UseHealingPotion(%PartyMemberIndex);
      if (isObject(MainCharacter))
      {
         $Opt::PotSeen = OptPotions();
         OptRefreshNow();
      }
   }

   // raiders set out (CheckForRaiderSpawn, Encounter::dailyUpdate): the guide answers at once
   function Building::spawnRaiders(%this, %raiderGroup, %numRaiders)
   {
      %r = Parent::spawnRaiders(%this, %raiderGroup, %numRaiders);
      OptRefreshNow();
      return %r;
   }

   // remembered in prefs, so the guide knows what you already have
   function UnlockAchievement(%name)
   {
      Parent::UnlockAchievement(%name);
      $pref::Opt::Have[%name] = 1;
      // counts at once; Steam's next answer (OptHave, within a minute) decides after that
      $Opt::HaveVal[%name] = 1;
      $Opt::HaveAsked[%name] = getRealTime();
   }

   // chests: world chests at game start and the treasure chest a cleared site leaves
   function spawnContainer(%dbContainer, %pos)
   {
      %c = Parent::spawnContainer(%dbContainer, %pos);
      if (isObject(%c) && isObject(OptLootSet))
      {
         OptLootSet.add(%c);
         // the treasure chest of a site being won belongs to that site wherever it lands
         if ($Opt::WinEnc !$= "") { %c.optSite = $Opt::WinEnc; }
      }
      return %c;
   }

   function Encounter::Win(%this)
   {
      $Opt::WinEnc = %this.getId();
      Parent::Win(%this);
      $Opt::WinEnc = "";
   }

   // the body a killed creature leaves, holding its gold, potions and dropped items
   function SpawnContainerFromCreature(%creature)
   {
      Parent::SpawnContainerFromCreature(%creature);
      OptLootCatchNew(%creature);
   }
};

// ------------------------------------------------------------------ setup

function OptInit()
{
   if (!isObject(PlayGui) || !isObject(GlobalActionMap))
   {
      schedule(2000, 0, OptInit);
      return;
   }
   OptCreateGui();
   if ($Opt::CamBase $= "" && isObject(IsoCameraData)) { $Opt::CamBase = IsoCameraData.Offset; }
   if ($Opt::TownCamBase $= "" && isObject(IsoCameraData)) { $Opt::TownCamBase = IsoCameraData.TownOffset; }
   OptTownZoomApply(OptTownZoom(), 1);
   if ($pref::Opt::ScreenWasd) { OptWasdBind(1); } else { OptArrowBind(1); }
   if ($pref::Opt::WheelZoom) { OptZoomBind(1); }
   OptLootSetMake();
   if (!$Opt::HooksOn)
   {
      activatePackage(OptHooksPackage);
      $Opt::HooksOn = 1;
   }
   GlobalActionMap.bindCmd(keyboard, "F8", "OptToggle();", "");
   GlobalActionMap.bindCmd(keyboard, "F7", "OptAutoKey();", "");
   cancel($Opt::Schedule);
   $Opt::Schedule = schedule($Opt::Refresh, 0, OptTick);
   cancel($Opt::FastSchedule);
   $Opt::FastSchedule = schedule($Opt::Fast, 0, OptFastTick);
   schedule(6000, 0, OptLogAchievements);
   echo("Hinterland Guide loaded (F8 to toggle)");
}

function OptCreateGui()
{
   // the window takes the mouse so it can be dragged; its text children do not (modal 0)
   if (!isObject(OptBgProfile))
   {
      new GuiControlProfile(OptBgProfile)
      {
         opaque = 1;
         fillColor = "10 10 14 205";
         border = 1;
         borderColor = "196 164 92 255";
         modal = 1;
      };
   }
   // the slider bar must take clicks, so it gets its own profile with modal on
   if (!isObject(OptBarProfile))
   {
      new GuiControlProfile(OptBarProfile)
      {
         opaque = 1;
         fillColor = "10 10 14 205";
         border = 1;
         borderColor = "196 164 92 255";
         modal = 1;
      };
   }
   if (!isObject(OptTextProfile))
   {
      new GuiControlProfile(OptTextProfile)
      {
         fontType = "Tahoma";
         fontSize = 14;
         fontSizeBase = 14;
         fontColor = "232 232 232";
         modal = 0;
      };
   }
   if (!isObject(OptOverlay))
   {
      new GuiMouseEventCtrl(OptOverlay)
      {
         Profile = "OptBgProfile";
         HorizSizing = "left";
         VertSizing = "bottom";
         position = "0" SPC $Opt::Top;
         Extent = OptWidth() SPC "200";
         Visible = 1;
         lockMouse = 1;
         new GuiBitmapCtrl(OptArrow)
         {
            Profile = "OptTextProfile";
            HorizSizing = "right";
            VertSizing = "bottom";
            position = "10 8";
            Extent = "48 48";
            bitmap = "tools/arrows/arrow_00";
            wrap = 0;
            Visible = 0;
         };
         new GuiMLTextCtrl(OptNowText)
         {
            Profile = "OptTextProfile";
            HorizSizing = "right";
            VertSizing = "bottom";
            position = "66 6";
            Extent = OptWidth() - 74 SPC "18";
            lineSpacing = 1;
            allowColorChars = 0;
            maxChars = -1;
         };
         new GuiMLTextCtrl(OptOverlayText)
         {
            Profile = "OptTextProfile";
            HorizSizing = "right";
            VertSizing = "bottom";
            position = "8 62";
            Extent = OptWidth() - 16 SPC "18";
            lineSpacing = 1;
            allowColorChars = 0;
            maxChars = -1;
         };
      };
   }
   if (!isObject(OptSliderBar))
   {
      new GuiControl(OptSliderBar)
      {
         Profile = "OptBarProfile";
         HorizSizing = "left";
         VertSizing = "bottom";
         position = "0" SPC $Opt::Top - 30;
         Extent = "250 28";
         Visible = 1;
         new GuiTextCtrl(OptSliderLabel)
         {
            Profile = "OptTextProfile";
            HorizSizing = "right";
            VertSizing = "bottom";
            position = "8 5";
            Extent = "70 18";
            text = "Text size";
         };
         new GuiSliderCtrl(OptFontSlider)
         {
            Profile = "GuiSliderProfile";
            HorizSizing = "right";
            VertSizing = "bottom";
            position = "82 2";
            Extent = "160 24";
            range = "10 30";
            ticks = 10;
            value = $pref::Opt::Font;
            Command = "OptSetFont(OptFontSlider.getValue());";
         };
      };
   }
   // two tickboxes in a bar above the text size bar: auto-attack and screen-true WASD (saved in
   // prefs). the tick image is the game's own (scriptsAndAssets/client/ui/demoCheck, as its
   // GuiCheckBoxProfile uses), with light text for the dark bar
   if (!isObject(OptCheckProfile))
   {
      new GuiControlProfile(OptCheckProfile)
      {
         opaque = 0;
         border = 0;
         fontType = "Tahoma";
         fontSize = 14;
         fontSizeBase = 14;
         fontColor = "232 232 232";
         fontColorHL = "255 217 102";
         fixedExtent = 1;
         justify = "left";
         bitmap = "scriptsAndAssets/client/ui/demoCheck";
         hasBitmapArray = 1;
         modal = 1;
      };
   }
   if (!isObject(OptTickBar))
   {
      new GuiControl(OptTickBar)
      {
         Profile = "OptBarProfile";
         HorizSizing = "left";
         VertSizing = "bottom";
         position = "0" SPC $Opt::Top - 58;
         Extent = "492 28";
         Visible = 1;
         new GuiCheckBoxCtrl(OptAutoAttackBox)
         {
            Profile = "OptCheckProfile";
            HorizSizing = "right";
            VertSizing = "bottom";
            position = "6 4";
            Extent = "117 20";
            text = "Auto-attack";
            buttonType = "ToggleButton";
            Command = "OptTicksChanged();";
         };
         new GuiCheckBoxCtrl(OptWasdBox)
         {
            Profile = "OptCheckProfile";
            HorizSizing = "right";
            VertSizing = "bottom";
            position = "127 4";
            Extent = "117 20";
            text = "Screen WASD";
            buttonType = "ToggleButton";
            Command = "OptTicksChanged();";
         };
         new GuiCheckBoxCtrl(OptZoomBox)
         {
            Profile = "OptCheckProfile";
            HorizSizing = "right";
            VertSizing = "bottom";
            position = "248 4";
            Extent = "117 20";
            text = "Wheel zoom";
            buttonType = "ToggleButton";
            Command = "OptTicksChanged();";
         };
         new GuiCheckBoxCtrl(OptPotionBox)
         {
            Profile = "OptCheckProfile";
            HorizSizing = "right";
            VertSizing = "bottom";
            position = "369 4";
            Extent = "117 20";
            text = "Auto Potion";
            buttonType = "ToggleButton";
            Command = "OptTicksChanged();";
         };
      };
      OptAutoAttackBox.setValue($pref::Opt::AutoAttack ? 1 : 0);
      OptWasdBox.setValue($pref::Opt::ScreenWasd ? 1 : 0);
      OptZoomBox.setValue($pref::Opt::WheelZoom ? 1 : 0);
      OptPotionBox.setValue($pref::Opt::AutoPotion ? 1 : 0);
      // new bars: placed again on the window's edges at the next draw
      $Opt::BarRect = "";
   }
   // the potion buttons above the tickboxes (OptPotionBarUpdate): the bar's own buttons, the
   // game's colours as the bars use them
   if (!isObject(OptButtonProfile))
   {
      new GuiControlProfile(OptButtonProfile)
      {
         opaque = 1;
         fillColor = "40 34 22 230";
         fillColorHL = "90 74 36 240";
         fillColorNA = "40 34 22 230";
         border = 1;
         borderColor = "196 164 92 255";
         fontType = "Tahoma";
         fontSize = 14;
         fontSizeBase = 14;
         fontColor = "232 232 232";
         fontColorHL = "255 217 102";
         justify = "center";
         canKeyFocus = 0;
         modal = 1;
      };
   }
   if (!isObject(OptPotionBar))
   {
      new GuiControl(OptPotionBar)
      {
         Profile = "OptBarProfile";
         HorizSizing = "left";
         VertSizing = "bottom";
         position = "0 0";
         Extent = "492 28";
         Visible = 0;
      };
      $Opt::PotionSig = "";
      $Opt::PotionN = 0;
      $Opt::BarRect = "";
   }
   OptOverlay.setVisible($Opt::Visible);
   OptSliderBar.setVisible($Opt::Visible);
   OptTickBar.setVisible($Opt::Visible);
   OptPotionBar.setVisible($Opt::Visible && $Opt::PotionN > 0);
}

// a tickbox was clicked: read both, save them, and switch the WASD keys over
function OptTicksChanged()
{
   $pref::Opt::AutoAttack = OptAutoAttackBox.getValue() ? 1 : 0;
   $pref::Opt::ScreenWasd = OptWasdBox.getValue() ? 1 : 0;
   $pref::Opt::WheelZoom = OptZoomBox.getValue() ? 1 : 0;
   $pref::Opt::AutoPotion = OptPotionBox.getValue() ? 1 : 0;
   OptWasdBind($pref::Opt::ScreenWasd);
   OptZoomBind($pref::Opt::WheelZoom);
   if (!$pref::Opt::AutoAttack)
   {
      // ANY CHASE STOPS (the player: unticked, the lord kept running to the enemy on his own):
      // the one auto-attack started, and any other attack target or step aside still going
      // always, even with no attack target left: he was stuck attacking with his target already
      // dead and gone, and a check for a target found nothing to stop
      OptAutoAttackDrop("auto-attack unticked");
      if (isObject(MainCharacter))
      {
         %t = MainCharacter.getAttackTarget();
         echo("Opt auto-attack: unticked, attack stopped" @ (isObject(%t) ? " (was on " @ OptCName(%t) @ ")" : ""));
         MainCharacter.stopAttacking();
         MainCharacter.setAttackTarget(0);
         MainCharacter.stop();
      }
   }
}

// WHEEL ZOOM (tickbox). the game's camera sits at your lord + IsoCameraData.Offset ("4.5 -4.5 10"
// in camera.cs) and has no zoom of its own. the game itself scales that offset at run time for
// its camera shake (UpdateCameraShake in GUIFX.cs: set IsoCameraData.Offset, then
// isoCamera.setCameraTarget(MainCharacter)); the wheel does the same: up = closer, down =
// farther, 10% a notch, from 0.6 to 3 times the game's distance, kept in prefs. unticked, the
// game's own distance comes back and the wheel is unbound
function OptZoomBind(%on)
{
   if (!isObject(moveMap)) { return; }
   if (%on) { moveMap.bind(mouse, zaxis, OptWheel); }
   else { moveMap.unbind(mouse, zaxis); }
   OptZoomApply(%on ? $pref::Opt::Zoom : 1);
}

function OptWheel(%val)
{
   if (%val == 0) { return; }
   // on the town map the wheel zooms the town map (its own distance, kept in prefs)
   if (isObject(gTown) && gTown.townView)
   {
      %t = OptTownZoom();
      %t = (%val > 0) ? %t / 1.1 : %t * 1.1;
      $pref::Opt::TownZoom = mClamp(%t, 0.6, 2.5);
      OptTownZoomApply($pref::Opt::TownZoom, 1);
      return;
   }
   %k = $pref::Opt::Zoom;
   if (%k $= "" || %k <= 0) { %k = 1; }
   %k = (%val > 0) ? %k / 1.1 : %k * 1.1;
   $pref::Opt::Zoom = mClamp(%k, 0.6, 3);
   OptZoomApply($pref::Opt::Zoom);
}

// the camera at %k times the game's distance (the game's own offset, read once before any change)
function OptZoomApply(%k)
{
   if (!isObject(IsoCameraData) || $gCameraShaking) { return; }
   if ($Opt::CamBase $= "") { $Opt::CamBase = IsoCameraData.Offset; }
   if (%k $= "" || %k <= 0) { %k = 1; }
   IsoCameraData.Offset = VectorScale($Opt::CamBase, %k);
   $Opt::ZoomApplied = %k;
   %client = isObject(MainCharacter) ? MainCharacter.client : 0;
   if (isObject(%client) && isObject(%client.isoCamera)) { %client.isoCamera.setCameraTarget(MainCharacter); }
}

// TOWN MAP ZOOM (the player: "i cannot have a full view of the town when selecting Hire a
// townsfolk"; with 26 of 28 lots built, the two free ones, lots 0 and 1, were off the top of the
// screen, so their house could not be placed). the town map's camera sits at the town center
// (+ "10 -10 0", game.cs) + IsoCameraData.TownOffset ("22 -22 55", camera.cs); with the game's
// 80 degree view on a wide screen it shows about 42-47 m up the screen from that center, while
// the basic town's lots (TownLayoutBasic.cs) reach 63 m up, 29 m down and 47 m sideways. 1.6
// times the game's distance shows them all ($Opt::TownZoomFit, worked out for a 1920 x 991 view).
// the wheel changes it while the town map is open (0.6 to 2.5, kept in prefs)
$Opt::TownZoomFit = 1.6;

function OptTownZoom()
{
   %t = $pref::Opt::TownZoom;
   if (%t $= "" || %t <= 0) { %t = $Opt::TownZoomFit; }
   return %t;
}

// the town map's camera at %k times the game's distance; %now: the town map is open, so the
// camera is set to it again to take the new distance
function OptTownZoomApply(%k, %now)
{
   if (!isObject(IsoCameraData)) { return; }
   if ($Opt::TownCamBase $= "") { $Opt::TownCamBase = IsoCameraData.TownOffset; }
   if (getWordCount($Opt::TownCamBase) < 3) { return; }
   if (%k $= "" || %k <= 0) { %k = 1; }
   IsoCameraData.TownOffset = VectorScale($Opt::TownCamBase, %k);
   %client = isObject(MainCharacter) ? MainCharacter.client : 0;
   if (%now && isObject(gTown) && gTown.townView && isObject(%client) && isObject(%client.isoCamera)) { %client.isoCamera.setTownViewMode(); }
}

// SCREEN WASD. the game moves W/A/S/D along the map's own north/west/south/east (moveForward,
// moveleft... in default.bind.cs set $mvForwardAction etc.), and its slanted camera shows map
// north as up-right on the screen. ticked, the four keys are bound to the guide's own handlers:
// the keys held make a screen direction (W up, A left, S down, D right), which is turned into
// the map direction that shows as it (the camera's projection, as the arrow uses), and given to
// the same move values. unticked, the game's own handlers are bound back
// ARROW KEYS TOO (the player: "let's make arrows movement also available besides WASD"; the game
// binds nothing to them): up, left, down and right move like W, A, S and D, ticked or not, each
// with its own held flag so letting go of one key never stops the other
function OptWasdBind(%on)
{
   if (!isObject(moveMap)) { return; }
   $Opt::KeyW = 0; $Opt::KeyA = 0; $Opt::KeyS = 0; $Opt::KeyD = 0;
   $Opt::KeyUp = 0; $Opt::KeyLeft = 0; $Opt::KeyDown = 0; $Opt::KeyRight = 0;
   $mvForwardAction = 0; $mvBackwardAction = 0; $mvLeftAction = 0; $mvRightAction = 0;
   if (%on)
   {
      moveMap.bind(keyboard, w, OptKeyW);
      moveMap.bind(keyboard, a, OptKeyA);
      moveMap.bind(keyboard, s, OptKeyS);
      moveMap.bind(keyboard, d, OptKeyD);
   }
   else
   {
      moveMap.bind(keyboard, w, moveForward);
      moveMap.bind(keyboard, a, moveleft);
      moveMap.bind(keyboard, s, movebackward);
      moveMap.bind(keyboard, d, moveright);
   }
   OptArrowBind(1);
}

// the arrow keys on (the guide's handlers) or off (unbound, as the game has them: closing Options
// saves moveMap to config.cs, and they must not be left there when the mod is removed)
function OptArrowBind(%on)
{
   if (!isObject(moveMap)) { return; }
   if (%on)
   {
      moveMap.bind(keyboard, "up", OptKeyUp);
      moveMap.bind(keyboard, "left", OptKeyLeft);
      moveMap.bind(keyboard, "down", OptKeyDown);
      moveMap.bind(keyboard, "right", OptKeyRight);
   }
   else
   {
      moveMap.unbind(keyboard, "up");
      moveMap.unbind(keyboard, "left");
      moveMap.unbind(keyboard, "down");
      moveMap.unbind(keyboard, "right");
   }
}

function OptKeyW(%val) { $Opt::KeyW = %val ? 1 : 0; OptWasdApply(); }
function OptKeyA(%val) { $Opt::KeyA = %val ? 1 : 0; OptWasdApply(); }
function OptKeyS(%val) { $Opt::KeyS = %val ? 1 : 0; OptWasdApply(); }
function OptKeyD(%val) { $Opt::KeyD = %val ? 1 : 0; OptWasdApply(); }
function OptKeyUp(%val) { $Opt::KeyUp = %val ? 1 : 0; OptArrowApply(); }
function OptKeyLeft(%val) { $Opt::KeyLeft = %val ? 1 : 0; OptArrowApply(); }
function OptKeyDown(%val) { $Opt::KeyDown = %val ? 1 : 0; OptArrowApply(); }
function OptKeyRight(%val) { $Opt::KeyRight = %val ? 1 : 0; OptArrowApply(); }

// an arrow key: with Screen WASD ticked it moves along the screen like W/A/S/D (OptWasdApply);
// unticked, along the map as the game's own W/A/S/D do
function OptArrowApply()
{
   if ($pref::Opt::ScreenWasd) { OptWasdApply(); return; }
   %sp = ($movementSpeed $= "") ? 1 : $movementSpeed;
   $mvForwardAction = $Opt::KeyUp * %sp;
   $mvBackwardAction = $Opt::KeyDown * %sp;
   $mvRightAction = $Opt::KeyRight * %sp;
   $mvLeftAction = $Opt::KeyLeft * %sp;
}

function OptWasdApply()
{
   if (isObject(gTown)) { gTown.leaveTownView(); }
   %su = OptMax($Opt::KeyW, $Opt::KeyUp) - OptMax($Opt::KeyS, $Opt::KeyDown);
   %sr = OptMax($Opt::KeyD, $Opt::KeyRight) - OptMax($Opt::KeyA, $Opt::KeyLeft);
   %wx = 0;
   %wy = 0;
   if (%su != 0 || %sr != 0)
   {
      %w = OptScreenToMap(%sr, %su);
      %wx = getWord(%w, 0);
      %wy = getWord(%w, 1);
   }
   %sp = ($movementSpeed $= "") ? 1 : $movementSpeed;
   $mvForwardAction = OptMax(%wy, 0) * %sp;
   $mvBackwardAction = OptMax(-%wy, 0) * %sp;
   $mvRightAction = OptMax(%wx, 0) * %sp;
   $mvLeftAction = OptMax(-%wx, 0) * %sp;
}

// the map direction (length 1) that shows on the screen as %right right and %up up. the view
// projects the lord's spot and 1 m east and north of it (PlayGui.project, screen y grows down);
// when it can't, the fixed camera: it sits at lord + IsoCameraData.Offset, so screen-up is -Offset
function OptScreenToMap(%right, %up)
{
   %a = 0;
   %b = 0;
   %done = 0;
   if (!$Opt::NoProject && isObject(PlayGui) && isObject(MainCharacter))
   {
      %p = MainCharacter.getPosition();
      %x = getWord(%p, 0);
      %y = getWord(%p, 1);
      %z = getWord(%p, 2);
      %p0 = PlayGui.project(%x SPC %y SPC %z);
      %pe = PlayGui.project((%x + 1) SPC %y SPC %z);
      %pn = PlayGui.project(%x SPC (%y + 1) SPC %z);
      if (getWordCount(%p0) >= 2 && getWordCount(%pe) >= 2 && getWordCount(%pn) >= 2)
      {
         %ex = getWord(%pe, 0) - getWord(%p0, 0);
         %ey = getWord(%pe, 1) - getWord(%p0, 1);
         %nx = getWord(%pn, 0) - getWord(%p0, 0);
         %ny = getWord(%pn, 1) - getWord(%p0, 1);
         %det = %ex * %ny - %nx * %ey;
         if (mAbs(%det) > 0.0001)
         {
            // %a east + %b north lands at (%right, -%up) on the screen
            %a = (%right * %ny + %nx * %up) / %det;
            %b = (-%ex * %up - %right * %ey) / %det;
            %done = 1;
         }
      }
   }
   if (!%done)
   {
      %ox = 4.5;
      %oy = -4.5;
      if (isObject(IsoCameraData) && getWordCount(IsoCameraData.Offset) >= 2)
      {
         %ox = getWord(IsoCameraData.Offset, 0);
         %oy = getWord(IsoCameraData.Offset, 1);
      }
      // screen-up is (-ox, -oy), screen-right is (-oy, ox) (see OptArrowIndex)
      %a = %up * -%ox + %right * -%oy;
      %b = %up * -%oy + %right * %ox;
   }
   %len = mSqrt(%a * %a + %b * %b);
   if (%len < 0.0001) { return "0 0"; }
   return (%a / %len) SPC (%b / %len);
}

// AUTO-ATTACK (tickbox). two jobs:
// 1. in a fight, when your target is dead (or you have none) and an enemy is still attacking
//    you, your lord is set on one of those (the ones that joined), until none is left: mages
//    first, then archers, then melee, the nearest of a kind (OptFoeRank); one of a higher kind
//    that attacks you also takes the place of the target auto-attack picked (OptAutoAttackBetter).
//    while the guide says to run or to go to town and heal: only HIT BACK while you stand still
//    (an enemy attacking you within your weapon's reach), nothing while you move, never a chase
// 2. out of a fight: while the guide's step is to attack an enemy at a site (an enemy it rated
//    SAFE or OK, $OptAutoAtk, set by OptHuntShow / OptSiteStep), and you stand inside that
//    site's 64 m square, your lord is set on that enemy (setAttackTarget, as the game does for
//    party members). it never starts a fight the guide doesn't send you to
// in town: only raiders inside the town, nearest first; nothing else moves you there. the target
// it set is let go (the game's own stopAttacking / setAttackTarget(0) / stop, as
// CreatureData::onDisabled does) when you untick the box, or in town when it is not a raider
function OptAutoAttack()
{
   if (!isObject(MainCharacter) || MainCharacter.getState() $= "Dead") { return; }
   // THE TARGET AUTO-ATTACK PICKED IS DEAD OR GONE (the player: "stuck in auto-attack, the enemy
   // is nowhere to be seen"; the Goblin it was on had just been killed): the attack is ended at
   // once, so your lord is not left attacking nothing
   if ($Opt::AutoTarget !$= "" && (!isObject($Opt::AutoTarget) || $Opt::AutoTarget.getState() $= "Dead"))
   {
      echo("Opt auto-attack: its target is dead or gone, attack ended");
      $Opt::AutoTarget = "";
      $Opt::AutoLogged = "";
      $Opt::UnstickTicks = 0;
      %t = MainCharacter.getAttackTarget();
      if (!isObject(%t) || %t.getState() $= "Dead")
      {
         MainCharacter.stopAttacking();
         MainCharacter.setAttackTarget(0);
      }
   }
   // a dead enemy still set as your attack target, whoever set it: ended too
   %dt = MainCharacter.getAttackTarget();
   if (isObject(%dt) && %dt.getState() $= "Dead")
   {
      echo("Opt auto-attack: your attack target " @ OptCName(%dt) @ " is dead, attack ended");
      MainCharacter.stopAttacking();
      MainCharacter.setAttackTarget(0);
   }
   if (OptLordInTown())
   {
      // RAIDERS IN TOWN, STOOD STILL (the player: "the raiders came to attack the town, auto-attack
      // is doing nothing, my character is just standing still"; it had picked a raider 20 m away
      // across the town). the stuck check (OptUnstick) only ran outside town: in town, among the
      // buildings and fences, a lord pressed against one stayed there. now it runs here too, and
      // a step aside under way is not undone by a new pick
      if ($Opt::UnstickTicks > 0) { OptUnstick(0); return; }
      %t = MainCharacter.getAttackTarget();
      if (isObject(%t) && %t.getState() !$= "Dead" && OptIsRaider(%t))
      {
         if (OptUnstick(%t)) { return; }
         OptAutoAttackBetter(%t, 1);
         return;
      }
      OptAutoAttackDrop();
      %t = MainCharacter.getAttackTarget();
      if (isObject(%t) && %t.getState() !$= "Dead") { return; }
      %r = OptRaiderInTown();
      if (isObject(%r)) { OptAutoAttackSet(%r); }
      return;
   }
   %t = MainCharacter.getAttackTarget();
   // RUNNING OR WALKING BACK TO HEAL. two player rules: "the game is chasing an enemy I had to run
   // from" and "I'm being attacked and the hero doesn't attack back". so while you stand still,
   // an enemy attacking you within your weapon's reach (OptLordReach) is hit back; while you
   // move (W/A/S/D held, or your lord walking on your click: more than 0.3 m since the last
   // tick, 150 ms, while not getting closer to the target) nothing is picked and auto-attack's
   // own target is let go, and one that falls out of reach is let go too: never walked after
   if (strstr($OptLastTitle, "Run") == 0 || strstr($OptLastTitle, "Go to town and heal") == 0)
   {
      %p = MainCharacter.getPosition();
      %mv = ($Opt::RunPos $= "") ? 0 : OptDist2D(%p, $Opt::RunPos);
      $Opt::RunPos = %p;
      %keys = $mvForwardAction != 0 || $mvBackwardAction != 0 || $mvLeftAction != 0 || $mvRightAction != 0;
      %reach = OptLordReach();
      if (isObject(%t) && %t.getState() !$= "Dead")
      {
         if ($Opt::AutoTarget $= "" || %t.getId() != $Opt::AutoTarget) { return; }
         %d = OptDist2D(%p, %t.getPosition());
         if (%keys || (%mv > 0.3 && %d >= $Opt::RunTD - 0.05)) { OptAutoAttackLetGo("you are moving"); return; }
         if (%d > %reach) { OptAutoAttackLetGo("out of reach"); return; }
         $Opt::RunTD = %d;
         return;
      }
      if (%keys || %mv > 0.3) { return; }
      %on = OptEnemyOnLord(%reach);
      if (isObject(%on))
      {
         OptAutoAttackSet(%on);
         $Opt::RunTD = OptDist2D(%p, %on.getPosition());
      }
      return;
   }
   $Opt::RunPos = "";
   // stuck on a tree or fence on the way to the target: a step aside first (OptUnstick)
   if (OptUnstick(%t)) { return; }
   // FAR TARGET LET GO (the player: "auto-attack gets glued to an enemy ... the enemy is miles
   // away yet it's still automatically running to him"): the enemy auto-attack picked is let go
   // once it is more than $Opt::AutoFar m away
   if (isObject(%t) && %t.getState() !$= "Dead" && $Opt::AutoTarget !$= "" && %t.getId() == $Opt::AutoTarget && OptDist2D(MainCharacter.getPosition(), %t.getPosition()) > $Opt::AutoFar)
   {
      OptAutoAttackDrop("more than " @ $Opt::AutoFar @ " m away");
      return;
   }
   if (isObject(%t) && %t.getState() !$= "Dead") { OptAutoAttackBetter(%t, 0); return; }
   %on = OptEnemyOnLord(20);
   if (isObject(%on)) { OptAutoAttackSet(%on); return; }
   if (MainCharacter.inCombat()) { return; }
   %c = $OptAutoAtk;
   %e = $OptAutoSite;
   if (%c $= "" || !isObject(%c) || %c.getState() $= "Dead" || !isObject(%e) || %e.won) { return; }
   %p = MainCharacter.getPosition();
   %ep = %e.encounterPosition;
   %dx = getWord(%p, 0) - getWord(%ep, 0);
   %dy = getWord(%p, 1) - getWord(%ep, 1);
   if (%dx < 0 || %dx > 64 || %dy < 0 || %dy > 64) { return; }
   if (OptDist2D(%p, %c.getPosition()) > $Opt::AutoFar) { return; }
   OptAutoAttackSet(%c);
}

// UNSTUCK (the player: "sometimes when auto-attacking, the character gets stuck in game objects
// (trees, fences, etc)"). the game walks your lord straight at the target auto-attack set
// (setAttackTarget) and he can stay pressed against a tree or fence. checked every fast tick
// (150 ms, none while the game is paused): the target auto-attack set is out of reach and your
// lord moved under 0.25 m a tick for 8 ticks in a row (about 1.2 s) = stuck. he is then sent 4 m
// to one side and 1.5 m back (setMoveDestination, the game's own click-to-move,
// serverCmdPlayerMoveTo), the side alternating each time, and after 6 ticks (about 0.9 s) the
// target is set again. 1 while a step aside is under way
function OptUnstick(%t)
{
   if ($Opt::UnstickTicks > 0)
   {
      $Opt::UnstickTicks--;
      if ($Opt::UnstickTicks == 0 && isObject($Opt::UnstickTarget) && $Opt::UnstickTarget.getState() !$= "Dead") { OptAutoAttackSet($Opt::UnstickTarget); }
      return 1;
   }
   if (!isObject(%t) || %t.getState() $= "Dead" || $Opt::AutoTarget $= "" || %t.getId() != $Opt::AutoTarget)
   {
      $Opt::StuckTicks = 0;
      $Opt::StuckPos = "";
      return 0;
   }
   %p = MainCharacter.getPosition();
   %tp = %t.getPosition();
   if (OptDist2D(%p, %tp) <= OptLordReach() + 0.5)
   {
      $Opt::StuckTicks = 0;
      $Opt::StuckPos = %p;
      return 0;
   }
   if ($Opt::StuckPos !$= "" && OptDist2D(%p, $Opt::StuckPos) < 0.25) { $Opt::StuckTicks++; }
   else { $Opt::StuckTicks = 0; }
   $Opt::StuckPos = %p;
   if ($Opt::StuckTicks < 8) { return 0; }
   $Opt::StuckTicks = 0;
   $Opt::StuckSide = ($Opt::StuckSide == 1) ? -1 : 1;
   %dx = getWord(%tp, 0) - getWord(%p, 0);
   %dy = getWord(%tp, 1) - getWord(%p, 1);
   %len = mSqrt(%dx * %dx + %dy * %dy);
   if (%len < 0.01) { return 0; }
   %dx /= %len;
   %dy /= %len;
   %x = getWord(%p, 0) - %dx * 1.5 - %dy * 4 * $Opt::StuckSide;
   %y = getWord(%p, 1) - %dy * 1.5 + %dx * 4 * $Opt::StuckSide;
   $Opt::UnstickTarget = %t.getId();
   MainCharacter.stopAttacking();
   MainCharacter.setAttackTarget(0);
   MainCharacter.setMoveDestination(%x SPC %y SPC getWord(%p, 2));
   $Opt::UnstickTicks = 6;
   echo("Opt unstick: stuck on the way to " @ OptCName(%t) @ " at " @ mFloor(getWord(%p, 0)) SPC mFloor(getWord(%p, 1)) @ ", stepping aside");
   return 1;
}

// how far an enemy auto-attack follows (m): beyond it the target is let go (OptAutoAttack)
$Opt::AutoFar = 30;

function OptAutoAttackSet(%c)
{
   MainCharacter.setAttackTarget(%c);
   $Opt::AutoTarget = %c.getId();
   // every new pick goes to console.log, so a chase that looks wrong can be read back
   if ($Opt::AutoLogged !$= %c.getId())
   {
      $Opt::AutoLogged = %c.getId();
      echo("Opt auto-attack: on " @ OptCName(%c) @ " (" @ getWord("mage archer melee", OptFoeRank(%c)) @ "), " @ mRound(OptDist2D(MainCharacter.getPosition(), %c.getPosition())) @ " m away");
   }
}

// how far your lord strikes from where he stands: the game's $MeleeWeaponMaxRange (3 m), or
// $MissileWeaponMaxRange (20 m) with a bow; a metre more for the two bodies
function OptLordReach()
{
   %w = OptEquipped(MainCharacter, 0);
   %bow = %w !$= "" && %w.getDataBlock().ActionType $= "missile attack";
   %r = %bow ? $MissileWeaponMaxRange : $MeleeWeaponMaxRange;
   if (%r $= "" || %r <= 0) { %r = %bow ? 20 : 3; }
   return %r + 1;
}

// lets go of the target auto-attack set, without stopping you: you keep running. %why for the log
function OptAutoAttackLetGo(%why)
{
   if ($Opt::AutoTarget $= "") { return; }
   %id = $Opt::AutoTarget;
   $Opt::AutoTarget = "";
   $Opt::AutoLogged = "";
   %t = MainCharacter.getAttackTarget();
   if (!isObject(%t) || %t.getId() != %id) { return; }
   echo("Opt auto-attack: let go of " @ OptCName(%t) @ " (" @ %why @ ")");
   MainCharacter.stopAttacking();
   MainCharacter.setAttackTarget(0);
}

// lets go of the target auto-attack set, only if your lord still has that one. %why for the log
function OptAutoAttackDrop(%why)
{
   $Opt::UnstickTicks = 0;
   if ($Opt::AutoTarget $= "") { return; }
   %id = $Opt::AutoTarget;
   $Opt::AutoTarget = "";
   $Opt::AutoLogged = "";
   if (!isObject(MainCharacter)) { return; }
   %t = MainCharacter.getAttackTarget();
   if (!isObject(%t) || %t.getId() != %id) { return; }
   echo("Opt auto-attack: let go of " @ OptCName(%t) @ (%why !$= "" ? " (" @ %why @ ")" : ""));
   MainCharacter.stopAttacking();
   MainCharacter.setAttackTarget(0);
   MainCharacter.stop();
}

// the living raider at the town (within 120 m of its centre), or 0: ranged first (OptFoeRank),
// the nearest within the same kind
function OptRaiderInTown()
{
   if (!isObject(gTown)) { return 0; }
   %lp = MainCharacter.getPosition();
   %best = 0;
   %bd = 999999;
   %br = 3;
   // the game's own nearby search around the town (see OptEnemyOnLord): never every enemy on
   // the map every 150 ms. 250 m reaches past the town's edge on every side
   // the raiders are gathered first and the town test comes after the search, so nothing
   // inside the search loop can start another search
   %n = 0;
   %town = gTown.getTownCenter();
   initContainerRadiusSearch(%town, 250, $TypeMasks::PlayerObjectType);
   while ((%c = containerSearchNext()) != 0)
   {
      if (%c.Role !$= "Enemy" || %c.getState() $= "Dead") { continue; }
      %found[%n] = %c;
      %n++;
   }
   for (%j = 0; %j < %n; %j++)
   {
      %c = %found[%j];
      if (!OptIsRaider(%c)) { continue; }
      %cp = %c.getPosition();
      // AT THE TOWN AS THE GUIDE'S RAID STEP SAYS IT (the player: "auto-attack doesn't always work
      // when raiders are in town, the hero doesn't attack them"; four Ogres raided with no pick
      // at all): within 120 m of the town centre, where raiders stage, steal and attack
      // (OptRaidThreat, "Defend the town: attack the raiders"). gTown.Contains ends well inside
      // that, so raiders at the outer buildings were never picked
      if (OptDist2D(%cp, %town) >= 120) { continue; }
      %d = OptDist2D(%lp, %cp);
      %k = OptFoeRank(%c);
      if (%k < %br || (%k == %br && %d < %bd)) { %br = %k; %bd = %d; %best = %c; }
   }
   return %best;
}

// the living enemy within %r metres whose attack target is your lord, or 0: ranged first (the
// player's order: mages, then archers, then melee, OptFoeRank), the nearest within the same kind
function OptEnemyOnLord(%r)
{
   %me = MainCharacter.getId();
   %lp = MainCharacter.getPosition();
   %best = 0;
   %bd = 999999;
   %br = 3;
   // THE GAME'S OWN NEARBY SEARCH (as CreatureData::yellForHelp does), not every enemy on the
   // map: this runs every 150 ms whenever you have no target, and a new map holds over 300
   // enemies. looping over all of them 7 times a second made the game stutter after every
   // kill; the perf line never showed it (it only timed the 2 s refresh)
   initContainerRadiusSearch(%lp, %r + 5, $TypeMasks::PlayerObjectType);
   while ((%c = containerSearchNext()) != 0)
   {
      if (%c.Role !$= "Enemy" || %c.getState() $= "Dead") { continue; }
      %t = %c.getAttackTarget();
      if (!isObject(%t) || %t.getId() != %me) { continue; }
      %d = OptDist2D(%lp, %c.getPosition());
      if (%d > %r) { continue; }
      %k = OptFoeRank(%c);
      if (%k < %br || (%k == %br && %d < %bd)) { %br = %k; %bd = %d; %best = %c; }
   }
   return %best;
}

// RANGED FIRST (the player: "when a bunch of enemies are attacking us, we need to prioritize
// ranged enemies first, because they deal the most dmg to us: mages/staves, archers, melee").
// 0 = a mage, 1 = an archer, 2 = melee. read the way the game shows an enemy's attack
// (Creature::getAttackIcon): the weapon it holds (slot 0), its AttackStatIcon magic (staffs,
// wands) or missile (bows). an enemy with no weapon fights in melee.
// A KIND THAT NEVER FLIPS: a dragon swaps its fang and its breath after most attacks
// (DragonProc::onEndAttack, the other one kept in .alternateWeapon), so two dragons kept trading
// places as "higher kind" and auto-attack switched between them every tick, ending each attack
// before it landed. an enemy counts as the best of the weapon it holds and the one it swaps to
function OptFoeRank(%c)
{
   %r = 2;
   %w = %c.Inventory[0];
   if (%w !$= "" && isObject(%w))
   {
      %db = %w.getDataBlock();
      %r = OptIconRank(%db.AttackStatIcon);
      // before its first swap a dragon has no .alternateWeapon yet: its fang means its breath
      if (%db.getName() $= "dbItem_Dragon_Fang" && isObject(dbItem_Dragon_Breath)) { %r = OptMin(%r, OptIconRank(dbItem_Dragon_Breath.AttackStatIcon)); }
   }
   %alt = %c.alternateWeapon;
   if (%alt !$= "" && isObject(%alt)) { %r = OptMin(%r, OptIconRank(%alt.getDataBlock().AttackStatIcon)); }
   return %r;
}

// a weapon's attack icon as auto-attack's kind: 0 magic, 1 missile, 2 melee
function OptIconRank(%ic)
{
   if (%ic $= $gStatIconMagic) { return 0; }
   if (%ic $= $gStatIconMissile) { return 1; }
   return 2;
}

// while your lord is on the target auto-attack picked, an enemy attacking you (within 20 m) of a
// higher kind (OptFoeRank: a mage over an archer over melee) takes its place. a target you
// clicked yourself is kept. %raid: only a raider may take its place (in town)
function OptAutoAttackBetter(%t, %raid)
{
   if ($Opt::AutoTarget $= "" || %t.getId() != $Opt::AutoTarget) { return; }
   %on = OptEnemyOnLord(20);
   if (!isObject(%on) || %on.getId() == %t.getId()) { return; }
   if (%raid && !OptIsRaider(%on)) { return; }
   if (OptFoeRank(%on) >= OptFoeRank(%t)) { return; }
   // the old attack is ended first: the game never changes a live attack target, it only sets
   // one when there is none (CreatureData::onPartyMemberAttacked), as auto-attack does after a kill
   echo("Opt auto-attack: switching from " @ OptCName(%t) @ " to a higher kind");
   MainCharacter.stopAttacking();
   MainCharacter.setAttackTarget(0);
   OptAutoAttackSet(%on);
}

// hold the window to drag it; the text size bar rides along above it
function OptOverlay::onMouseDown(%this, %mod, %pt, %clicks)
{
   %p = %this.getPosition();
   $Opt::DragDX = getWord(%pt, 0) - getWord(%p, 0);
   $Opt::DragDY = getWord(%pt, 1) - getWord(%p, 1);
   $Opt::Dragging = 1;
   $Opt::DragLast = getRealTime();
}

function OptOverlay::onMouseDragged(%this, %mod, %pt, %clicks)
{
   if (!$Opt::Dragging) { return; }
   $Opt::DragLast = getRealTime();
   OptMoveTo(getWord(%pt, 0) - $Opt::DragDX, getWord(%pt, 1) - $Opt::DragDY);
}

function OptOverlay::onMouseUp(%this, %mod, %pt, %clicks)
{
   $Opt::Dragging = 0;
}

// ends a drag whose mouse-up never came: the window, its slider and its tick boxes are taken off
// the screen and put back in the same order, which also makes the window give up the mouse
function OptEndDrag()
{
   $Opt::Dragging = 0;
   echo("Opt window: a drag was left on, ended");
   if (!isObject(OptOverlay)) { return; }
   %g = OptOverlay.getGroup();
   if (!isObject(%g)) { return; }
   %g.remove(OptOverlay);
   %g.add(OptOverlay);
   if (isObject(OptSliderBar)) { %g.remove(OptSliderBar); %g.add(OptSliderBar); }
   if (isObject(OptTickBar)) { %g.remove(OptTickBar); %g.add(OptTickBar); }
   if (isObject(OptPotionBar)) { %g.remove(OptPotionBar); %g.add(OptPotionBar); }
}

// F7: auto-attack on or off from the keyboard, the same as its tick box (works even when the
// window can't be clicked); it also ends a drag left on
function OptAutoKey()
{
   if ($Opt::Dragging) { OptEndDrag(); }
   if (!isObject(OptAutoAttackBox)) { return; }
   OptAutoAttackBox.setValue($pref::Opt::AutoAttack ? 0 : 1);
   OptTicksChanged();
   echo("Opt auto-attack: F7, now " @ ($pref::Opt::AutoAttack ? "on" : "off"));
}

function OptMoveTo(%x, %y)
{
   %cw = getWord(Canvas.getExtent(), 0);
   %ch = getWord(Canvas.getExtent(), 1);
   %w = getWord(OptOverlay.getExtent(), 0);
   %h = getWord(OptOverlay.getExtent(), 1);
   %x = mClamp(%x, 0, %cw - %w);
   %y = mClamp(%y, 58 + $Opt::PotionH, %ch - 40);
   $pref::Opt::Right = %cw - %x - %w;
   $pref::Opt::Y = %y;
   $pref::Opt::Placed = 1;
   $Opt::Right = $pref::Opt::Right;
   $Opt::Top = %y;
   OptOverlay.resize(%x, %y, %w, %h);
   OptPlaceBars(%x, %y, %w);
}

// F8: one step -> step + details -> hidden
function OptToggle()
{
   $pref::Opt::Mode = ($pref::Opt::Mode + 1) % 3;
   $Opt::Visible = ($pref::Opt::Mode != 2);
   if (isObject(OptOverlay))
   {
      OptOverlay.setVisible($Opt::Visible);
      OptSliderBar.setVisible($Opt::Visible);
      OptTickBar.setVisible($Opt::Visible);
      if (isObject(OptPotionBar)) { OptPotionBar.setVisible($Opt::Visible && $Opt::PotionN > 0); }
   }
   if (!$Opt::Visible) { OptJunkMarks(0); }
   if ($Opt::Visible)
   {
      OptTick();
   }
}

// window width follows the text size; never narrower than the four tickboxes need (117 each,
// 492 with the gaps; 372 for three before Auto Potion), since the tickbox bar and the text size
// bar take the same width (OptPlaceBars)
function OptWidth()
{
   %f = $pref::Opt::Font;
   if (%f < 10) { %f = 18; }
   return OptMax(mRound(29 * %f), 492);
}

// the tickbox bar and the text size bar sit straight above the guide window, with its left and
// right edges and its width. the three tickboxes share the bar's width equally. the slider keeps
// its size and its distance from the right edge, so it stays under the mouse while you drag it
// (dragging changes the text size, and so the width)
function OptPlaceBars(%x, %y, %w)
{
   if (!isObject(OptSliderBar) || !isObject(OptTickBar)) { return; }
   $Opt::BarX = %x;
   $Opt::BarY = %y;
   $Opt::BarW = %w;
   %key = %x SPC %y SPC %w;
   if (%key $= $Opt::BarRect) { return; }
   $Opt::BarRect = %key;
   // the potion buttons above the tickboxes: two to a row, all the same size
   $Opt::PotionH = 0;
   if (isObject(OptPotionBar))
   {
      %pn = OptPotionBar.getCount();
      %rows = mFloor((%pn + 1) / 2);
      %ph = %rows * 24 + 4;
      if (%pn > 0) { $Opt::PotionH = %ph; }
      OptPotionBar.resize(%x, %y - 58 - %ph, %w, %ph);
      %pw = mFloor((%w - 16) / 2);
      for (%i = 0; %i < %pn; %i++) { OptPotionBar.getObject(%i).resize(6 + (%i % 2) * (%pw + 4), 4 + mFloor(%i / 2) * 24, %pw, 20); }
      OptPotionBar.setVisible($Opt::Visible && %pn > 0);
   }
   OptSliderBar.resize(%x, %y - 30, %w, 28);
   OptFontSlider.resize(%w - 168, 2, 160, 24);
   OptTickBar.resize(%x, %y - 58, %w, 28);
   %bw = mFloor((%w - 24) / 4);
   OptAutoAttackBox.resize(6, 4, %bw, 20);
   OptWasdBox.resize(10 + %bw, 4, %bw, 20);
   OptZoomBox.resize(14 + 2 * %bw, 4, %bw, 20);
   if (isObject(OptPotionBox)) { OptPotionBox.resize(18 + 3 * %bw, 4, %bw, 20); }
}

// called by the slider while it is dragged
function OptSetFont(%v)
{
   %v = mClamp(mRound(%v), 10, 30);
   if (%v == $pref::Opt::Font) { return; }
   $pref::Opt::Font = %v;
   if ($OptLastBody !$= "") { OptShow($OptLastTarget, $OptLastTitle, $OptLastInfo, $OptLastBody); }
}

// a paused game ($timeScale 0, as pauseGame sets it) runs no timed pieces, so the plan is worked
// out in one go then; nothing moves meanwhile. unpaused, it is spread over frames as usual
// a window opened or closed, or you did what the step said: the guide answers in this same
// frame, paused or not. a pass already running is first finished on the spot, each stage in
// full, so nothing half-built is left behind. then a fresh one-frame pass ALWAYS follows: the
// running pass may have made its plan before the window opened (it was past its plan stage),
// and showing that as the answer left "Click the level-up button" up while the level-up window
// was open, for good, because a paused game runs no timed pass after it
function OptRefreshNow()
{
   if ($Opt::Busy && $Opt::Stage !$= "keep" && $Opt::Visible && OptGameReady())
   {
      cancel($Opt::PipeSchedule);
      $Opt::PipeGen++;
      $Opt::PipeSync = 1;
      for (%guard = 0; $Opt::Busy && %guard < 5000; %guard++) { OptPipeStep(); }
      $Opt::Busy = 0;
   }
   $Opt::ForceSync = 1;
   OptTick();
   $Opt::ForceSync = 0;
}

// the hire / townsperson window now shows someone else: refresh once per person
function OptTalkerSeen(%which, %talker)
{
   if ($Opt::Talker[%which] $= %talker) { return; }
   $Opt::Talker[%which] = %talker;
   if (%talker !$= "") { OptRefreshNow(); }
}

function OptAttach(%gui)
{
   if (OptOverlay.getGroup() != %gui.getId())
   {
      %gui.add(OptOverlay);
      %gui.add(OptSliderBar);
      %gui.add(OptTickBar);
      %gui.add(OptPotionBar);
   }
   %cw = getWord(Canvas.getExtent(), 0);
   if ($pref::Opt::Placed)
   {
      $Opt::Top = $pref::Opt::Y;
      $Opt::Right = $pref::Opt::Right;
   }
   else
   {
      // sit below the lord's stats panel (PartyMember1 in RightBorder, 196 of 1200 design pixels tall)
      if (%gui.getId() == PlayGui.getId() && isObject(PartyMember1) && isObject(RightBorder))
      {
         %bottom = getWord(RightBorder.getPosition(), 1) + getWord(PartyMember1.getPosition(), 1) + getWord(PartyMember1.getExtent(), 1);
      }
      else
      {
         %bottom = mRound(196 * getWord(Canvas.getExtent(), 1) / 1200);
      }
      $Opt::Top = %bottom + 72;
      $Opt::Right = 8;
   }
   // the bars above the window, on its edges (where OptShow puts the window)
   %w = OptWidth();
   OptPlaceBars(mClamp(%cw - %w - $Opt::Right, 0, OptMax(%cw - %w, 0)), $Opt::Top, %w);
   OptOverlay.setVisible($Opt::Visible);
   OptSliderBar.setVisible($Opt::Visible);
   OptTickBar.setVisible($Opt::Visible);
   OptPotionBar.setVisible($Opt::Visible && $Opt::PotionN > 0);
}

function OptTick()
{
   cancel($Opt::Schedule);
   $Opt::Schedule = schedule($Opt::Refresh, 0, OptTick);
   $Opt::LastTick = getRealTime();
   if (!$Opt::Visible || !isObject(Canvas))
   {
      return;
   }
   if (!isObject(OptOverlay) || !isObject(OptTickBar))
   {
      OptCreateGui();
   }
   %content = Canvas.getContent();
   if (isObject(createCharacterGui) && %content == createCharacterGui.getId())
   {
      OptAttach(createCharacterGui);
      OptShow("", OptSetupNow(), "", OptSetupText());
      return;
   }
   if (!isObject(PlayGui) || %content != PlayGui.getId())
   {
      return;
   }
   if (!isObject(MainCharacter) || !isObject(gTown) || !isObject(Encounters) || !$CreationComplete)
   {
      return;
   }
   OptAttach(PlayGui);
   if ($pref::Opt::WheelZoom && $pref::Opt::Zoom !$= "" && $Opt::ZoomCam !$= MainCharacter.getId())
   {
      $Opt::ZoomCam = MainCharacter.getId();
      OptZoomApply($pref::Opt::Zoom);
   }
   if ($gCombatMethod != 0)
   {
      OptShow("", "HINTERLAND GUIDE", "", "Combat method is " @ $gCombatMethod @ ". The guide only models method 0.\n");
      return;
   }
   // a new or loaded game: find the chests and bodies already in the world, a few per frame
   if ($Opt::LootScanFor !$= MainCharacter.getId()) { OptLootScanStart(); }
   // a pass still running is left to finish (a stuck one is restarted after 5 s). the keep check
   // runs after the guide is shown, in the time left: a new pass simply takes over from it
   if (!$Opt::ForceSync && $Opt::Busy && $Opt::Stage !$= "keep" && getRealTime() - $Opt::PipeStartTime < 5000)
   {
      return;
   }
   OptPipeStart($Opt::ForceSync);
}

// ------------------------------------------------------------------ plan pipeline
// stages: sites (rate every site, a few per piece) > sort > gear (try every item, a few per
// piece) > pull (nearby groups, only when no site is SAFE or OK) > plan > show

function OptGameReady()
{
   return isObject(Canvas) && isObject(PlayGui) && Canvas.getContent() == PlayGui.getId() && isObject(MainCharacter) && isObject(gTown) && isObject(Encounters) && $CreationComplete;
}

function OptPipeStart(%sync)
{
   cancel($Opt::PipeSchedule);
   $Opt::PipeGen++;
   $Opt::Busy = 1;
   $Opt::PipeSync = %sync;
   $Opt::PipeStartTime = getRealTime();
   OptFixStaleGear();
   OptLoadParty();
   OptKeepFortPick();
   OptPortalScan();
   $OptScanLP = MainCharacter.getPosition();
   $OptScanI = 0;
   $OptScanN = 0;
   $OptNearN = 0;
   $OptTopRaw = -1;
   $OptTopIdx = -1;
   $OptGearReady = 0;
   // a paused (one-frame) pass skips the heavy stages and keeps their last results: loot, the
   // keep check, the makers' items and the target search (see OptPipeAfterGear / AfterLoot)
   $OptPullReady = 0;
   $OptPullResult = "";
   $Opt::Stage = "sites";
   if (%sync)
   {
      %t0 = getRealTime();
      // a stage that cannot move on (a script error inside it) must not freeze the game
      for (%guard = 0; $Opt::Busy && %guard < 5000; %guard++) { OptPipeStep(); }
      $Opt::Busy = 0;
      OptPerfNote(getRealTime() - %t0, 1, "paused");
      if (getRealTime() - %t0 > 250) { echo("Opt slow: paused refresh took " @ (getRealTime() - %t0) @ " ms"); }
      return;
   }
   $Opt::PipeSchedule = schedule($Opt::SliceGap, 0, OptPipeTick, $Opt::PipeGen);
}

function OptPipeTick(%gen)
{
   if (%gen != $Opt::PipeGen || !$Opt::Busy) { return; }
   if (!$Opt::Visible || !OptGameReady()) { $Opt::Busy = 0; return; }
   %stage = $Opt::Stage;
   %t0 = getRealTime();
   OptPipeStep();
   OptPerfNote(getRealTime() - %t0, 0, %stage);
   // any slow piece is named in console.log at once
   if (getRealTime() - %t0 > 250) { echo("Opt slow: " @ %stage @ " piece took " @ (getRealTime() - %t0) @ " ms"); }
   if ($Opt::Busy) { $Opt::PipeSchedule = schedule($Opt::SliceGap, 0, OptPipeTick, $Opt::PipeGen); }
}

// true while this piece still has time left and has done fewer than %cap things
// (the cap matters where the clock only ticks every 15 ms). a sync pass never stops early
function OptSliceLeft(%t0, %done, %cap)
{
   return $Opt::PipeSync || (%done < %cap && getRealTime() - %t0 < $Opt::SliceBudget);
}

function OptPipeStep()
{
   %s = $Opt::Stage;
   if (%s $= "sites") { OptPipeSites(); }
   else if (%s $= "sort") { OptPipeSort(); }
   else if (%s $= "gear") { OptPipeGear(); }
   else if (%s $= "keep") { OptPipeKeep(); }
   else if (%s $= "psets") { OptPipePSets(); }
   else if (%s $= "loot") { OptPipeLoot(); }
   else if (%s $= "hunt") { OptPipeHunt(); }
   else if (%s $= "plan") { OptPipePlan(); }
   else if (%s $= "text") { OptPipeText(); }
   else if (%s $= "show") { OptPipeShow(); }
   else { $Opt::Busy = 0; }
}

function OptPipeSites()
{
   %t0 = getRealTime();
   %cnt = Encounters.getCount();
   %done = 0;
   while ($OptScanI < %cnt && OptSliceLeft(%t0, %done, 6))
   {
      %e = Encounters.getObject($OptScanI);
      $OptScanI++;
      if (!isObject(%e) || %e.won) { continue; }
      OptScanOne(%e, $OptScanN, $OptScanLP);
      OptSiteInsert($OptScanN);
      $OptScanN++;
      %done++;
   }
   if ($OptScanI >= %cnt)
   {
      $OptSiteN = $OptScanN;
      $Opt::Stage = "sort";
   }
}

function OptPipeSort()
{
   OptSiteFinish();
   %ge = OptGearSitePick();
   if (isObject(%ge))
   {
      $OptGearSite = %ge;
      OptGearBegin($OptGearSite);
      $Opt::Stage = "gear";
   }
   else
   {
      OptPipeAfterGear();
   }
}

function OptPipeGear()
{
   if (OptGearStep(getRealTime()))
   {
      $OptGearReady = 1;
      // paused: one frame does it all, so the makers' items wait for the next normal pass
      if ($Opt::PipeSync) { OptPipeAfterGear(); return; }
      // what the town's makers could give is scored against the gear site, every 30 s
      if ($OptPSTime $= "" || getRealTime() - $OptPSTime > 30000)
      {
         OptPSBegin();
         $Opt::Stage = "psets";
         return;
      }
      OptPipeAfterGear();
   }
}

// KEEP CHECK: is a stored item better than your worn gear in ANY fight still ahead? every item is
// tested against every unwon site twice, at full HP: the whole site, and its strongest enemy one
// on one (fights you can't survive are skipped). an item that loses everywhere is junk; one that
// wins anywhere is kept. results are saved and only redone when your stats, worn gear or the
// sites left change; new items are added as they come. it runs after the guide is on screen
// (OptPipeShow), one fight or two per piece, at most $Opt::KeepPerPass fights per pass, and the
// next pass takes over from it, so the guide never waits for it (the first full check takes a
// few passes)
// 120 (was 60), doubled with $Opt::SliceBudget: the same share of each pass, twice the fights
$Opt::KeepPerPass = 120;

// the every-enemy totals (they decide the equip step), then the keep check.
// JUNK ITEM BY ITEM (the player: "my town inventory is full of gear, but the mod never tells me to
// sell it"; after a weapon change the totals took 127 s of play, and the keep check only began
// after all of them, so nothing was junk for minutes). while the totals have work left, the two
// take turns piece by piece whenever the keep check's next item already has its own total
// (OptKeepReady): each item gets both answers one after the other and can be junk at once
function OptPipeKeep()
{
   %t0 = getRealTime();
   if ($OptWGGen !$= "" && OptWGPending())
   {
      $OptKeepTurn = !$OptKeepTurn;
      if ($OptKeepTurn && OptKeepReady()) { OptKeepStep(%t0); return; }
      OptWGStep(%t0);
      return;
   }
   if (!OptWGStep(%t0)) { return; }
   if (OptKeepStep(%t0)) { $Opt::Busy = 0; }
}

// 1 when the keep check has a fight to do this pass on an item whose every-enemy total is done
function OptKeepReady()
{
   if ($OptKeepFights >= $Opt::KeepPerPass) { return 0; }
   while ($OptKeepII < $OptKeepIN && !isObject($OptKeepItem[$OptKeepII])) { $OptKeepII++; }
   if ($OptKeepII >= $OptKeepIN) { return 0; }
   return OptWGItemSum($OptKeepItem[$OptKeepII].getId()) !$= "";
}

// starts this pass's share of the keep check: a new list of fights when anything it depends on
// changed, then the items still missing results
function OptKeepBegin()
{
   %L = $OptLord;
   %c = $OptObj["P", %L];
   $OptKeepFights = 0;
   $OptKeepIN = 0;
   $OptKeepII = 0;
   if (!isObject(%c)) { return; }
   %sig = $OptA["P", %L] @ "_" @ $OptD["P", %L] @ "_" @ $OptT["P", %L] @ "_" @ $OptMHP["P", %L] @ "_" @ $OptCB["P", %L];
   for (%s = 0; %s < 4; %s++) { %sig = %sig @ "_" @ %c.Inventory[%s]; }
   // a won site doesn't restart the check (its fights are skipped, and an item kept only for it
   // is checked again, OptKeepAdd); a new site (a raider fort) does
   %sig = %sig @ "_" @ Encounters.getCount();
   if (%sig !$= $OptKeepSig)
   {
      // a new list of fights: every unwon site, whole and its strongest enemy alone
      $OptKeepSig = %sig;
      $OptKeepGen++;
      $OptKeepBN = 0;
      for (%i = 0; %i < $OptSiteN; %i++)
      {
         $OptKeepBSite[$OptKeepBN] = $OptSite[%i];
         $OptKeepBTop[$OptKeepBN] = 1;
         $OptKeepBBase[$OptKeepBN] = "";
         $OptKeepBN++;
         $OptKeepBSite[$OptKeepBN] = $OptSite[%i];
         $OptKeepBTop[$OptKeepBN] = 0;
         $OptKeepBBase[$OptKeepBN] = "";
         $OptKeepBN++;
      }
   }
   // the items to work on this pass: wearable gear in both inventories without a full result
   $OptKeepAllN = 0;
   OptInvList();
   for (%v = 0; %v < $OptInvN; %v++)
   {
      %inv = $OptInv[%v];
      %slots = %inv.GetNumSlots();
      for (%k = 0; %k < %slots; %k++)
      {
         %it = %inv.GetItem(%k);
         if (!isObject(%it)) { continue; }
         %idb = %it.getDataBlock();
         if (%idb.Useable) { continue; }
         if (%idb.category $= "Weapon" && %idb.ActionType !$= "melee attack" && %idb.ActionType !$= "missile attack") { continue; }
         %slot = OptItemSlot(%c, %idb);
         if (%slot $= "" || (OptHandsBlocked(%c, %idb, %slot) && !OptOffFor2H(%c, %idb, %slot))) { continue; }
         OptKeepAdd(%it, %slot, " (" @ $OptInvSrc[%v] @ ")", "", "");
      }
   }
   // and what your townsfolk wear (not work items, not what your gear beats in every stat)
   for (%i = 0; %i < TownsPeople.getCount(); %i++)
   {
      %p = TownsPeople.getObject(%i);
      if (!isObject(%p) || %p.getState() $= "Dead" || MainCharacter.getParty().isMember(%p)) { continue; }
      %prof = %p.myCandidateData.Profession;
      for (%ps = 0; %ps < 4; %ps++)
      {
         %it = OptEquipped(%p, %ps);
         if (!isObject(%it)) { continue; }
         %idb = %it.getDataBlock();
         if (%idb.Useable || OptWorkBonus(%idb, %prof) > 0) { continue; }
         if (%idb.category $= "Weapon" && %idb.ActionType !$= "melee attack" && %idb.ActionType !$= "missile attack") { continue; }
         %slot = OptItemSlot(%c, %idb);
         if (%slot $= "" || (OptHandsBlocked(%c, %idb, %slot) && !OptOffFor2H(%c, %idb, %slot))) { continue; }
         %cur = OptEquipped(%c, %slot);
         if (isObject(%cur) && %cur.getId() == %it.getId()) { continue; }
         if (OptBetterWorn(%c, %slot, %it) !$= "") { continue; }
         OptKeepAdd(%it, %slot, " (worn by " @ %p.getNameString() @ ")", %p, %ps);
      }
   }
}

// puts an item on the keep check: a fresh result when anything changed, and on this pass's list
// while results are missing. remembers where it is (%src, or the townsperson %who in slot %ws)
function OptKeepAdd(%it, %slot, %src, %who, %ws)
{
   %id = %it.getId();
   // the fight it was kept for is won: checked again
   if ($OptKeepGenOf[%id] == $OptKeepGen && OptKeepForWon(%id)) { $OptKeepGenOf[%id] = -1; }
   if ($OptKeepGenOf[%id] != $OptKeepGen)
   {
      $OptKeepGenOf[%id] = $OptKeepGen;
      $OptKeepUpTo[%id] = 0;
      $OptKeepMin[%id] = 0;
      $OptKeepMax[%id] = 0;
      $OptKeepCmp[%id] = 0;
      $OptKeepWhere[%id] = "";
      $OptKeepWhereB[%id] = "";
   }
   $OptKeepSlotOf[%id] = %slot;
   $OptKeepSrcOf[%id] = %src;
   $OptKeepWhoOf[%id] = %who;
   $OptKeepWhoSlotOf[%id] = %ws;
   $OptKeepAll[$OptKeepAllN] = %it;
   $OptKeepAllN++;
   if ($OptKeepUpTo[%id] < $OptKeepBN)
   {
      $OptKeepItem[$OptKeepIN] = %it;
      $OptKeepSlot[$OptKeepIN] = %slot;
      $OptKeepIN++;
   }
}

// a game glitch: dragging an item off a townsperson onto you can leave it listed in their slot
// with its stats still counted on them. an item a townsperson lists that you wear, or that lies
// in the bag or town inventory, is taken off them with the game's own unequip (it clears the
// slot and their stats), once it has stayed that way for 3 s (never during a drag in progress)
function OptFixStaleGear()
{
   if (!isObject(TownsPeople) || !isObject(MainCharacter)) { return; }
   $OptStaleGen++;
   for (%s = 0; %s < 4; %s++)
   {
      %x = OptEquipped(MainCharacter, %s);
      if (%x !$= "") { $OptStaleMark[%x] = $OptStaleGen; }
   }
   OptInvList();
   for (%v = 0; %v < $OptInvN; %v++)
   {
      %inv = $OptInv[%v];
      %slots = %inv.GetNumSlots();
      for (%k = 0; %k < %slots; %k++)
      {
         %it = %inv.GetItem(%k);
         if (isObject(%it)) { $OptStaleMark[%it.getId()] = $OptStaleGen; }
      }
   }
   %now = getRealTime();
   for (%i = 0; %i < TownsPeople.getCount(); %i++)
   {
      %p = TownsPeople.getObject(%i);
      if (!isObject(%p) || %p.getState() $= "Dead") { continue; }
      %pid = %p.getId();
      for (%ps = 0; %ps < 4; %ps++)
      {
         %it = OptEquipped(%p, %ps);
         if (%it $= "" || $OptStaleMark[%it] != $OptStaleGen) { $OptStaleSeen[%pid, %ps] = ""; continue; }
         %seen = $OptStaleSeen[%pid, %ps];
         if (getWord(%seen, 0) !$= %it) { $OptStaleSeen[%pid, %ps] = %it SPC %now; continue; }
         if (%now - getWord(%seen, 1) < 3000) { continue; }
         echo("Opt fixed: " @ %it.getNameString() @ " was still listed on " @ %p.getNameString() @ " (with its stats) though it is with you");
         %p.unequipItem(%it);
         $OptStaleSeen[%pid, %ps] = "";
      }
   }
}

// WHICH ITEM TO WEAR is decided over every enemy left on the map, never by the next fight (that
// answer changed with the target: helm against one enemy, ring against another, and back). for
// every item you could wear, and for taking off what you wear, the guide adds up the damage your
// party takes killing each living enemy one at a time at full HP, with it and as you are now.
// sites not seen up close count with their expected enemies. a fight that costs more than your
// full HP counts as your full HP, so one lost either way counts the same for both:
//    total(what you wear) = sum over the enemies of min(damage taken, full HP)
// the lower total is the item to wear. every swap the guide asks for lowers that one number, so
// it can never ask for the way back, wherever you stand and whatever the target is.
// worked out again when your party's numbers or your worn items change, or a site is won; the
// last finished totals (OptWGPublish) are used only while they were made for what you wear now.
// enemies with the same numbers are fought once and counted as many times. runs after the guide
// is on screen, ahead of the keep check (OptPipeKeep), a few fights per piece

// what the totals depend on besides the map: the party's numbers and what your lord wears
function OptWGSetSig()
{
   %sig = $OptN["P"];
   for (%k = 0; %k < $OptN["P"]; %k++)
   {
      %sig = %sig @ "_" @ $OptA["P", %k] @ "/" @ $OptD["P", %k] @ "/" @ $OptT["P", %k] @ "/" @ $OptMHP["P", %k] @ "/" @ $OptCB["P", %k];
   }
   %c = $OptObj["P", $OptLord];
   if (isObject(%c))
   {
      for (%s = 0; %s < 4; %s++) { %sig = %sig @ "_" @ %c.Inventory[%s]; }
   }
   return %sig;
}

// 1 when finished totals exist for exactly what you wear now
function OptWGFresh()
{
   return $OptWGPubSet !$= "" && $OptWGPubSet $= OptWGSetSig();
}

// starts this pass's share: a new list of enemies when anything it depends on changed, then
// every item on the keep check's list (bag, town inventory, townsfolk) and "take off" for what
// your lord wears in the armor, off-hand and artifact slots
function OptWGBegin()
{
   %L = $OptLord;
   %c = $OptObj["P", %L];
   $OptWGN = 0;
   $OptWGI = 0;
   if (!isObject(%c) || !isObject(Encounters)) { return; }
   %set = OptWGSetSig();
   %sig = %set @ "_" @ Encounters.getCount() @ "_" @ $OptSiteN;
   if (%sig !$= $OptWGSig)
   {
      $OptWGSig = %sig;
      $OptWGSet = %set;
      $OptWGGen++;
      $OptWGStartMs = $Opt::PlayMs;
      $OptWGPhase = 0;
      $OptWGEI = 0;
      $OptWGSI = 0;
      $OptWGFN = 0;
      $OptWGEnemies = 0;
      $OptWGBeat = 0;
      // full HP without what your lord's own items add: the same number whatever is worn
      %cap = 0;
      for (%k = 0; %k < $OptN["P"]; %k++) { %cap += $OptMHP["P", %k]; }
      for (%s = 0; %s < 4; %s++)
      {
         %w = OptEquipped(%c, %s);
         if (%w !$= "") { %cap -= %w.getHealth(); }
      }
      $OptWGCap = %cap;
   }
   for (%n = 0; %n < $OptKeepAllN; %n++)
   {
      %it = $OptKeepAll[%n];
      if (isObject(%it)) { OptWGAdd(%it.getId(), %it, $OptKeepSlotOf[%it.getId()], 0); }
   }
   for (%s = 1; %s < 4; %s++)
   {
      %w = OptEquipped(%c, %s);
      if (%w !$= "") { OptWGAdd("rm" @ %s, %w, %s, 1); }
   }
}

function OptWGAdd(%key, %it, %slot, %rm)
{
   if ($OptWGGenOf[%key] != $OptWGGen)
   {
      $OptWGGenOf[%key] = $OptWGGen;
      $OptWGUpTo[%key] = 0;
      $OptWGSum[%key] = 0;
      $OptWGMax[%key] = 0;
   }
   $OptWGKey[$OptWGN] = %key;
   $OptWGItem[$OptWGN] = %it;
   $OptWGSlot[$OptWGN] = %slot;
   $OptWGRm[$OptWGN] = %rm;
   $OptWGN++;
}

// the enemy loaded at place %j of group E goes on the list: one entry per set of numbers, with
// a count of how many enemies have them
function OptWGAddFight(%j)
{
   %key = $OptA["E", %j] @ "_" @ $OptD["E", %j] @ "_" @ $OptMHP["E", %j] @ "_" @ $OptT["E", %j] @ "_" @ $OptCB["E", %j] @ "_" @ $OptLv["E", %j] @ "_" @ $OptFx["E", %j] @ "_" @ $OptFxW["E", %j];
   %key = strreplace(%key, " ", "/");
   $OptWGEnemies++;
   if ($OptWGKeyGen[%key] == $OptWGGen)
   {
      $OptWGFW[$OptWGKeyIdx[%key]]++;
      return;
   }
   %f = $OptWGFN;
   $OptWGKeyGen[%key] = $OptWGGen;
   $OptWGKeyIdx[%key] = %f;
   $OptWGFA[%f] = $OptA["E", %j];
   $OptWGFD[%f] = $OptD["E", %j];
   $OptWGFH[%f] = $OptMHP["E", %j];
   $OptWGFT[%f] = $OptT["E", %j];
   $OptWGFC[%f] = $OptCB["E", %j];
   $OptWGFL[%f] = $OptLv["E", %j];
   $OptWGFX[%f] = $OptFx["E", %j];
   $OptWGFY[%f] = $OptFxW["E", %j];
   $OptWGFW[%f] = 1;
   $OptWGFBase[%f] = "";
   $OptWGFN++;
}

// list entry %f alone into group E, at full health
function OptWGLoad(%f)
{
   $OptA["E", 0] = $OptWGFA[%f];
   $OptD["E", 0] = $OptWGFD[%f];
   $OptHP["E", 0] = $OptWGFH[%f];
   $OptMHP["E", 0] = $OptWGFH[%f];
   $OptT["E", 0] = $OptWGFT[%f];
   $OptCB["E", 0] = $OptWGFC[%f];
   $OptNm["E", 0] = "enemy";
   $OptObj["E", 0] = 0;
   $OptLv["E", 0] = $OptWGFL[%f];
   $OptFx["E", 0] = $OptWGFX[%f];
   $OptFxW["E", 0] = $OptWGFY[%f];
   $OptN["E"] = 1;
}

// one piece, your lord at full HP (the real HP is back before the piece ends); 1 when this
// pass's share is done
function OptWGStep(%t0)
{
   %L = $OptLord;
   %c = $OptObj["P", %L];
   if (!isObject(%c) || $OptWGGen $= "") { return 1; }
   %hp = $OptHP["P", %L];
   $OptHP["P", %L] = $OptMHP["P", %L];
   %r = OptWGWork(%t0, %L, %c);
   $OptHP["P", %L] = %hp;
   return %r;
}

function OptWGWork(%t0, %L, %c)
{
   if (!OptWGList(%t0)) { return 0; }
   return OptWGItems(%t0, %L, %c);
}

// the list of every enemy left, whole, right now: for a level-up or ability pick, which is
// answered in one frame and can't wait for the pieces. a new list when your numbers or the
// sites changed (OptWGBegin), built to the end
function OptWGListNow()
{
   OptWGBegin();
   if ($OptWGGen $= "") { return; }
   %ps = $Opt::PipeSync;
   $Opt::PipeSync = 1;
   OptWGList(getRealTime());
   $Opt::PipeSync = %ps;
}

// THE SHARE OF YOUR HEALTH BAR A FIGHT COSTS, added up over every enemy on the list: damage
// taken killing it alone, divided by your party's full HP as loaded now (your lord's HP as set
// by the caller, so +HP choices count), a lost fight counting as the whole bar. the measure for
// level-ups and abilities; with HP unchanged it ranks like the gear totals
function OptWGShare()
{
   %L = $OptLord;
   %h = 0;
   for (%k = 0; %k < $OptN["P"]; %k++) { %h += (%k == %L) ? $OptHP["P", %k] : $OptMHP["P", %k]; }
   if (%h <= 0) { return 0; }
   %t = 0;
   for (%f = 0; %f < $OptWGFN; %f++)
   {
      OptWGLoad(%f);
      %d = getWord(OptFight(), 0);
      %x = (%d $= "inf") ? 1 : %d / %h;
      if (%x > 1) { %x = 1; }
      %t += %x * $OptWGFW[%f];
   }
   return %t;
}

// one piece of the list; 1 when it is whole
function OptWGList(%t0)
{
   %done = 0;
   // the list: every living enemy on the map
   if ($OptWGPhase == 0)
   {
      %cnt = isObject(enemyFactionSet) ? enemyFactionSet.getCount() : 0;
      while ($OptWGEI < %cnt)
      {
         if (!OptSliceLeft(%t0, %done, 30)) { return 0; }
         %e = enemyFactionSet.getObject($OptWGEI);
         $OptWGEI++;
         %done++;
         if (!isObject(%e) || %e.getState() $= "Dead") { continue; }
         OptSetUnit("E", 0, %e);
         OptWGAddFight(0);
      }
      $OptWGPhase = 1;
   }
   // and the expected enemies of every site not seen up close yet
   if ($OptWGPhase == 1)
   {
      %cnt = Encounters.getCount();
      while ($OptWGSI < %cnt)
      {
         if (!OptSliceLeft(%t0, %done, 3)) { return 0; }
         %site = Encounters.getObject($OptWGSI);
         $OptWGSI++;
         %done++;
         if (!isObject(%site) || %site.won || (%site.spawnPlaced && isObject(%site.memberSet))) { continue; }
         OptLoadSite(%site);
         %en = $OptN["E"];
         for (%j = 0; %j < %en; %j++) { OptWGAddFight(%j); }
      }
      $OptWGPhase = 2;
   }
   return 1;
}

// one piece of the items; 1 when this pass's share is done
function OptWGItems(%t0, %L, %c)
{
   %done = 0;
   // every item against every enemy on the list
   %cap = $OptWGCap;
   while ($OptWGI < $OptWGN)
   {
      %key = $OptWGKey[$OptWGI];
      %it = $OptWGItem[$OptWGI];
      %slot = $OptWGSlot[$OptWGI];
      %rm = $OptWGRm[$OptWGI];
      if (!isObject(%it)) { $OptWGI++; continue; }
      %cur = OptEquipped(%c, %slot);
      while ($OptWGUpTo[%key] < $OptWGFN)
      {
         if (!OptSliceLeft(%t0, %done, 6)) { return 0; }
         %f = $OptWGUpTo[%key];
         $OptWGUpTo[%key]++;
         OptWGLoad(%f);
         // as you are now: worked out once per enemy
         %base = $OptWGFBase[%f];
         if (%base $= "")
         {
            %base = getWord(OptFight(), 0);
            if (%base $= "inf" || %base > %cap) { %base = %cap; }
            $OptWGFBase[%f] = %base;
            if (%base < %cap) { $OptWGBeat += $OptWGFW[%f]; }
            %done++;
         }
         if (%rm)
         {
            OptGDSwap("", 0);
            OptGearDelta(%L, %c, %slot, %cur, 0, 0, 0, 0, 0);
         }
         else { OptGearScore(%L, %c, %slot, %cur, %it); }
         %new = $OptGDNew;
         if (%new > %cap) { %new = %cap; }
         %d = %new - %base;
         $OptWGSum[%key] += %d * $OptWGFW[%f];
         if (%d > $OptWGMax[%key]) { $OptWGMax[%key] = %d; }
         %done++;
      }
      $OptWGI++;
   }
   OptWGPublish();
   return 1;
}

// how far the every-enemy totals are, as "45%": fights done of items x kinds of enemy ("0%"
// while the list of enemies is still being made)
function OptWGProgress()
{
   if ($OptWGPhase < 2 || $OptWGFN < 1 || $OptWGN < 1) { return "0%"; }
   %done = 0;
   for (%n = 0; %n < $OptWGN; %n++) { %done += OptMin($OptWGUpTo[$OptWGKey[%n]], $OptWGFN); }
   return mFloor(100 * %done / ($OptWGN * $OptWGFN)) @ "%";
}

// 1 while the totals have work left: the list of enemies still being made, or an item (one just
// picked up) not yet fought against all of it
function OptWGPending()
{
   if ($OptWGPhase < 2) { return 1; }
   for (%n = 0; %n < $OptWGN; %n++) { if ($OptWGUpTo[$OptWGKey[%n]] < $OptWGFN) { return 1; } }
   return 0;
}

// one item's every-enemy total once it is done for what you wear now (the list of enemies whole
// and the item fought against all of it), else "": junk is judged item by item, without waiting
// for every other item (OptJunkAll)
function OptWGItemSum(%id)
{
   if ($OptWGPhase < 2 || $OptWGFN < 1 || $OptWGGenOf[%id] != $OptWGGen || $OptWGUpTo[%id] < $OptWGFN) { return ""; }
   if ($OptWGSet !$= OptWGSetSig()) { return ""; }
   return $OptWGSum[%id];
}

// every item has its total: these are the numbers the guide uses from now on
function OptWGPublish()
{
   // once per new set of totals: how big it was and how long it took, so a gear step that comes
   // late can be read back (the player suspected the totals take long)
   if ($OptWGLoggedGen !$= $OptWGGen)
   {
      $OptWGLoggedGen = $OptWGGen;
      echo("Opt gear totals done: " @ $OptWGN @ " items x " @ $OptWGFN @ " kinds of enemy (" @ $OptWGEnemies @ " enemies) in " @ mRound(($Opt::PlayMs - $OptWGStartMs) / 1000) @ " s of play");
   }
   $OptWGPubTag++;
   $OptWGPubSet = $OptWGSet;
   $OptWGPubN = $OptWGN;
   $OptWGPubEnemies = $OptWGEnemies;
   $OptWGPubBeat = $OptWGBeat;
   for (%n = 0; %n < $OptWGN; %n++)
   {
      %key = $OptWGKey[%n];
      $OptWGPubItem[%n] = $OptWGItem[%n];
      $OptWGPubSlot[%n] = $OptWGSlot[%n];
      $OptWGPubRm[%n] = $OptWGRm[%n];
      $OptWGPubSum[%n] = $OptWGSum[%key];
      $OptWGPubMax[%n] = $OptWGMax[%key];
      if (!$OptWGRm[%n])
      {
         $OptWGPubTagOf[%key] = $OptWGPubTag;
         $OptWGPubSumOf[%key] = $OptWGSum[%key];
      }
   }
}

// where an item lies right now: " (player inventory)", " (town inventory)", or "" (neither)
function OptItemSrcNow(%it)
{
   %id = %it.getId();
   OptInvList();
   for (%v = 0; %v < $OptInvN; %v++)
   {
      %inv = $OptInv[%v];
      %slots = %inv.GetNumSlots();
      for (%k = 0; %k < %slots; %k++)
      {
         %x = %inv.GetItem(%k);
         if (isObject(%x) && %x.getId() == %id) { return " (" @ $OptInvSrc[%v] @ ")"; }
      }
   }
   return "";
}

// the swap that lowers your total over every enemy left the most, 1 when there is one. it must
// save 0.1 HP per enemy you can beat now on average (so two near-equal items never trade places
// as the map changes), or be never worse against any enemy and save 0.5 in all.
// what is in town (its inventory, what townsfolk wear) is only offered while you are in town:
// the guide never sends you back to town for an item.
// sets $OptWPItem, $OptWPSlot, $OptWPRm (take it off), $OptWPSum (the change, negative),
// $OptWPSrc (where it is), $OptWPWho / $OptWPWhoSlot (the townsperson wearing it)
function OptWholePick()
{
   %c = $OptObj["P", $OptLord];
   if (!isObject(%c) || !OptWGFresh()) { return 0; }
   %inTown = OptLordInTown();
   %tau = OptMax(0.5, 0.1 * $OptWGPubBeat);
   for (%try = 0; %try < 6; %try++)
   {
      %best = -1;
      %bs = 0;
      for (%n = 0; %n < $OptWGPubN; %n++)
      {
         if (%rej[%n]) { continue; }
         %sum = $OptWGPubSum[%n];
         if (%sum > -%tau && (%sum > -0.5 || $OptWGPubMax[%n] > 0.05)) { continue; }
         if (%best < 0 || %sum < %bs) { %best = %n; %bs = %sum; }
      }
      if (%best < 0) { return 0; }
      %it = $OptWGPubItem[%best];
      %slot = $OptWGPubSlot[%best];
      %ok = 0;
      %src = "";
      %who = "";
      %ws = "";
      if (isObject(%it))
      {
         %id = %it.getId();
         %cur = OptEquipped(%c, %slot);
         if ($OptWGPubRm[%best])
         {
            %ok = (%cur !$= "" && %cur == %id);
         }
         else if (%cur $= "" || %cur != %id)
         {
            %who = $OptKeepWhoOf[%id];
            if (%who !$= "" && isObject(%who) && OptEquipped(%who, $OptKeepWhoSlotOf[%id]) == %id)
            {
               %ws = $OptKeepWhoSlotOf[%id];
               %src = " (worn by " @ %who.getNameString() @ ")";
               %ok = %inTown;
            }
            else
            {
               %who = "";
               %src = OptItemSrcNow(%it);
               %ok = (%src !$= "") && (%inTown || %src !$= " (town inventory)");
            }
         }
      }
      if (%ok)
      {
         $OptWPItem = %it;
         $OptWPSlot = %slot;
         $OptWPRm = $OptWGPubRm[%best];
         $OptWPSum = %bs;
         $OptWPSrc = %src;
         $OptWPWho = %who;
         $OptWPWhoSlot = %ws;
         return 1;
      }
      %rej[%best] = 1;
   }
   return 0;
}

// one piece at full HP (your real HP is back before the piece ends); 1 when this pass's share is done
function OptKeepStep(%t0)
{
   %L = $OptLord;
   %hp = $OptHP["P", %L];
   $OptHP["P", %L] = $OptMHP["P", %L];
   %r = OptKeepWork(%t0);
   $OptHP["P", %L] = %hp;
   return %r;
}

function OptKeepWork(%t0)
{
   %L = $OptLord;
   %c = $OptObj["P", %L];
   if (!isObject(%c)) { return 1; }
   %done = 0;
   while ($OptKeepII < $OptKeepIN)
   {
      %it = $OptKeepItem[$OptKeepII];
      if (!isObject(%it)) { $OptKeepII++; continue; }
      %id = %it.getId();
      // while the every-enemy totals are being added up, an item waits for its own (OptPipeKeep)
      if ($OptWGGen !$= "" && OptWGPending() && OptWGItemSum(%id) $= "") { return 0; }
      %slot = $OptKeepSlot[$OptKeepII];
      %idb = %it.getDataBlock();
      while ($OptKeepUpTo[%id] < $OptKeepBN)
      {
         if ($OptKeepFights >= $Opt::KeepPerPass) { return 1; }
         if (!OptSliceLeft(%t0, %done, 2)) { return 0; }
         %b = $OptKeepUpTo[%id];
         %e = $OptKeepBSite[%b];
         $OptKeepUpTo[%id]++;
         if (!isObject(%e) || %e.won) { continue; }
         if ($OptKeepBTop[%b]) { OptLoadTopEnemy(%e); } else { OptLoadSite(%e); }
         if ($OptN["E"] < 1) { continue; }
         // the fight as worn is redone whenever the enemies there changed (one killed, one hurt):
         // an item is only ever compared with the same enemies
         %esig = $OptN["E"];
         for (%q = 0; %q < $OptN["E"]; %q++) { %esig = %esig @ "_" @ mRound($OptHP["E", %q]); }
         %base = $OptKeepBBase[%b];
         if (%base $= "" || $OptKeepBSig[%b] !$= %esig)
         {
            %base = getWord(OptFight(), 0);
            if (%base $= "inf") { %base = 9999; }
            $OptKeepBBase[%b] = %base;
            $OptKeepBSig[%b] = %esig;
            $OptKeepFights++;
            %done++;
         }
         // a fight you can't survive says nothing about gear
         if (%base >= 9999) { continue; }
         $OptKeepFights++;
         %done++;
         %cur = OptEquipped(%c, %slot);
         OptGearScore(%L, %c, %slot, %cur, %it);
         %d = $OptGDNew - %base;
         $OptKeepCmp[%id]++;
         if (%d > $OptKeepMax[%id]) { $OptKeepMax[%id] = %d; }
         if (%d < $OptKeepMin[%id])
         {
            $OptKeepMin[%id] = %d;
            $OptKeepWhere[%id] = ($OptKeepBTop[%b] ? "its strongest enemy at " : "") @ OptSiteName(%e);
            $OptKeepWhereB[%id] = %b;
         }
      }
      $OptKeepII++;
   }
   return 1;
}

// the keep check's verdict on an item: "junk", "keep <where it helps>", or "" (not fully checked,
// or no fight ahead you could survive to compare it in)
function OptKeepVerdict(%it)
{
   %id = %it.getId();
   if ($OptKeepGenOf[%id] != $OptKeepGen || $OptKeepUpTo[%id] < $OptKeepBN || $OptKeepCmp[%id] < 1) { return ""; }
   if (OptKeepForWon(%id)) { return ""; }
   if ($OptKeepMin[%id] < -0.05) { return "keep " @ $OptKeepWhere[%id]; }
   return "junk";
}

// 1 when the fight an item did best in belongs to a site you have won since
function OptKeepForWon(%id)
{
   %wb = $OptKeepWhereB[%id];
   if (%wb $= "") { return 0; }
   %e = $OptKeepBSite[%wb];
   return !isObject(%e) || %e.won;
}

function OptPipePSets()
{
   if (OptPSStep(getRealTime())) { OptPipeAfterGear(); }
}

// loot is read while the gear site is still loaded, so items in it can be scored as gear
function OptPipeAfterGear()
{
   // paused: the loot list from the last normal pass stands
   if ($Opt::PipeSync) { OptPipeAfterLoot(); return; }
   // not ready only while the list is being remade (a one-frame pass before this point still
   // has the last full list)
   $OptLootReady = 0;
   if (isObject(OptLootSet) && OptLootSet.getCount() > 0)
   {
      OptLootBegin();
      $Opt::Stage = "loot";
      return;
   }
   OptPipeAfterLoot();
}

function OptPipeLoot()
{
   if (OptLootStep(getRealTime()))
   {
      $OptLootReady = 1;
      OptPipeAfterLoot();
   }
}

// where to fight: the target search (OptHuntBegin) runs on every pass
function OptPipeAfterLoot()
{
   // a one-frame pass: the last search's site stands (found again by its id: the site list was
   // redone). unless its answer is gone: the enemy it named is dead, its site is won, or there
   // was no search yet. then the search runs now, in this frame. showing the old answer gave
   // "Nothing here you can beat yet" for a second or two after every kill, until the next full pass
   if ($Opt::PipeSync)
   {
      $OptHuntSite = OptSiteIndex($OptGoSite);
      if ($OptHuntSite < 0) { $OptHuntKind = ""; }
      %gone = ($OptHuntKind $= "") || ($OptHuntKind $= "hunt" && (!isObject($OptHuntObj) || $OptHuntObj.getState() $= "Dead"));
      if (%gone && $OptSiteN > 0)
      {
         OptHuntBegin();
         OptHuntStep(getRealTime());
      }
      $Opt::Stage = "plan";
      return;
   }
   if ($OptSiteN > 0)
   {
      OptHuntBegin();
      $Opt::Stage = "hunt";
      return;
   }
   $OptHuntKind = "";
   $OptHuntObj = 0;
   $OptHuntSite = -1;
   $Opt::Stage = "plan";
}

function OptPipeHunt()
{
   if (OptHuntStep(getRealTime())) { $Opt::Stage = "plan"; }
}

function OptPipePlan()
{
   OptLoadParty();
   OptPlan();
   // the site was only found in the target search's second round: say why you pass a harder one
   if ($OptHuntPast && $OptNowSite >= 0 && $OptNowSite == $OptHuntSite)
   {
      $OptNowInfo = $OptNowInfo @ "\nBehind a harder site: nothing you can reach without passing one can be beaten yet";
   }
   $Opt::Stage = "text";
}

function OptPipeText()
{
   OptBuildText();
   $Opt::Stage = "show";
}

function OptPipeShow()
{
   $OptShowNoRoute = $OptNowNoRoute;
   OptShow($OptNowTarget, $OptNowTitle, $OptNowInfo, $OptBody);
   // every change of the step goes to console.log with the target and your HP, so a sequence
   // that confused the player can be read back instead of guessed
   if ($OptNowTitle !$= $Opt::LoggedStep)
   {
      $Opt::LoggedStep = $OptNowTitle;
      %ls = ($OptHuntKind !$= "" && $OptHuntSite >= 0 && $OptHuntSite < $OptSiteN) ? $OptHuntKind @ " " @ OptSiteName($OptSite[$OptHuntSite]) @ " (" @ $OptSiteDanger[$OptHuntSite] @ " sq)" : "none";
      echo("Opt step: " @ $OptNowTitle @ " | target: " @ %ls @ " | HP " @ mFloor(MainCharacter.GetCurrentHealth()) @ "/" @ MainCharacter.getMaxHealth() @ " | at " @ mFloor(getWord(MainCharacter.getPosition(), 0)) SPC mFloor(getWord(MainCharacter.getPosition(), 1)) @ (OptLordInTown() ? " (town)" : "") @ " | " @ getSubStr(strreplace($OptNowInfo, "\n", " / "), 0, 160));
   }
   OptJunkMarks($OptJunkStep);
   // the guide is on screen: the keep check gets the time left before the next pass (never in a
   // paused one-frame pass)
   if (!$Opt::PipeSync)
   {
      OptKeepBegin();
      OptWGBegin();
      $Opt::Stage = "keep";
      return;
   }
   $Opt::Busy = 0;
}

// worst piece of work in one frame, written to console.log once a minute while playing
function OptPerfNote(%ms, %sync, %what)
{
   if (%sync)
   {
      if (%ms > $Opt::PerfSync) { $Opt::PerfSync = %ms; }
   }
   else
   {
      if (%ms > $Opt::PerfMax) { $Opt::PerfMax = %ms; $Opt::PerfWhat = %what; }
      // the worst time of each kind of piece, so the log names what is slow
      if (%ms > $Opt::PerfStage[%what]) { $Opt::PerfStage[%what] = %ms; }
   }
   if ($Opt::PerfLogT $= "") { $Opt::PerfLogT = getRealTime(); }
   if (getRealTime() - $Opt::PerfLogT >= 60000)
   {
      %each = "";
      %names = "sites sort gear keep psets loot hunt plan text show redraw tick lootscan";
      for (%i = 0; %i < getWordCount(%names); %i++)
      {
         %n = getWord(%names, %i);
         %each = %each @ " " @ %n @ "=" @ ($Opt::PerfStage[%n] + 0);
         $Opt::PerfStage[%n] = 0;
      }
      echo("Opt perf: worst frame " @ ($Opt::PerfMax + 0) @ " ms (" @ $Opt::PerfWhat @ "), worst paused refresh " @ ($Opt::PerfSync + 0) @ " ms, per piece:" @ %each);
      $Opt::PerfMax = 0;
      $Opt::PerfWhat = "";
      $Opt::PerfSync = 0;
      $Opt::PerfLogT = getRealTime();
   }
}

function OptShow(%target, %title, %info, %body)
{
   $OptLastTarget = %target;
   $OptLastTitle = %title;
   $OptLastInfo = %info;
   $OptLastBody = %body;
   %f = $pref::Opt::Font;
   if (%f < 10) { %f = 18; }
   // a portal on the way, worked out at draw time like the distance, so right after you step
   // through one the arrow already points on from where you came out
   // (no-route 1: straight there, no portal; 2: the run from a lost fight, portal allowed)
   if (%target !$= "" && $OptShowNoRoute != 1 && $OptPortN > 0 && isObject(MainCharacter))
   {
      %go = OptRoute(MainCharacter.getPosition(), %target);
      if ($OptRouteVia > 0 && $OptShowNoRoute == 2)
      {
         %info = "click it: it takes you to town, " @ mRound($OptRouteSave) @ "m less running" @ (%info $= "" ? "" : "\n" @ %info);
         %title = "Run to the portal, you lose this fight";
         %target = %go;
      }
      else if ($OptRouteVia > 0)
      {
         %info = "then: " @ %title @ " · saves " @ mRound($OptRouteSave) @ "m of walking" @ ($OptRouteVia > 1 ? " (two portals, through town)" : "") @ (%info $= "" ? "" : "\n" @ %info);
         %title = "Click the portal";
         %target = %go;
      }
   }
   // direction and distance are worked out here, at draw time, so they follow you between plans
   if (%target !$= "" && isObject(MainCharacter)) { %title = %title @ " (" @ OptWhere(%target) @ ")"; }
   if ($OptThenPos !$= "" && isObject(MainCharacter)) { %body = strreplace(%body, "@THEN@", OptWhere($OptThenPos)); }
   // a flashing warning: red and yellow by turns, every half second
   if (strstr(%body, "@FLASH@") >= 0) { %body = strreplace(%body, "@FLASH@", (mFloor(getRealTime() / 500) % 2) ? $Opt::Red : $Opt::Yellow); }
   %nowText = "<font:Tahoma Bold:" @ (%f + 2) @ ">" @ $Opt::Gold @ %title @ "<font:Tahoma:" @ %f @ ">" @ $Opt::White @ (%info $= "" ? "" : "\n" @ %info);
   %body = "<font:Tahoma:" @ %f @ ">" @ $Opt::White @ %body;
   %w = OptWidth();
   %idx = -1;
   if (%target !$= "" && isObject(MainCharacter))
   {
      %idx = OptArrowIndex(MainCharacter.getPosition(), %target);
   }
   // only what changed is redrawn: laying out text is the costly part
   if (%idx != $Opt::ShownIdx)
   {
      if (%idx >= 0)
      {
         OptArrow.setBitmap("tools/arrows/arrow_" @ (%idx < 10 ? "0" : "") @ %idx);
         OptArrow.setVisible(1);
      }
      else
      {
         OptArrow.setVisible(0);
      }
   }
   %nx = (%idx >= 0) ? 66 : 8;
   %layout = %w SPC %nx;
   %nowChanged = (%nowText !$= $Opt::ShownNow || %layout !$= $Opt::ShownLayout);
   if (%nowChanged)
   {
      OptNowText.setText(%nowText);
      OptNowText.resize(%nx, 6, %w - %nx - 8, 18);
      OptNowText.forceReflow();
      $Opt::ShownNow = %nowText;
   }
   %top = 6 + getWord(OptNowText.getExtent(), 1) + 6;
   if (%idx >= 0 && %top < 62) { %top = 62; }
   if (%body !$= $Opt::ShownBody || %top != $Opt::ShownTop || %layout !$= $Opt::ShownLayout)
   {
      OptOverlayText.setText(%body);
      OptOverlayText.resize(8, %top, %w - 16, 18);
      OptOverlayText.forceReflow();
      $Opt::ShownBody = %body;
      $Opt::ShownTop = %top;
   }
   $Opt::ShownIdx = %idx;
   $Opt::ShownLayout = %layout;
   %h = %top + getWord(OptOverlayText.getExtent(), 1) + 8;
   %cw = getWord(Canvas.getExtent(), 0);
   // a wider text size or a smaller screen must not push the window off the left edge
   %rect = mClamp(%cw - %w - $Opt::Right, 0, OptMax(%cw - %w, 0)) SPC $Opt::Top SPC %w SPC %h;
   if (%rect !$= $Opt::ShownRect)
   {
      OptOverlay.resize(getWord(%rect, 0), getWord(%rect, 1), %w, %h);
      $Opt::ShownRect = %rect;
   }
   OptPlaceBars(getWord(%rect, 0), getWord(%rect, 1), %w);
}

// redraws the arrow, the distance in the step line and the "Then:" distance from the
// last plan, without replanning
// AUTO POTION (tickbox; the player: "a tick box Auto Potion which auto health potions whenever
// less than 50% HP"). every fast tick (150 ms, none while paused): your lord under half his max
// HP, alive, a Healing Potion in your bag -> one is drunk with the game's own key-1 call
// (UseHealingPotion: the first Healing Potion in the bag, +a third of max HP). never in town (the
// player's rule: "from the moment I enter town, drink a potion should NEVER appear", OptPotionTown:
// resting there heals for free). 1 s between two, so a heal lands before the next look
function OptAutoPotion()
{
   if (!isObject(MainCharacter) || MainCharacter.getState() $= "Dead") { return; }
   if ($Opt::PotionLast !$= "" && getRealTime() - $Opt::PotionLast < 1000) { return; }
   %mhp = MainCharacter.getMaxHealth();
   %hp = MainCharacter.GetCurrentHealth();
   if (%mhp <= 0 || %hp >= %mhp * 0.5 || OptPotionTown() || OptPotions() < 1) { return; }
   %party = MainCharacter.getParty();
   if (!isObject(%party)) { return; }
   %me = MainCharacter.getId();
   for (%i = 0; %i < %party.getPartySize(); %i++)
   {
      %m = %party.getMember(%i);
      if (!isObject(%m) || %m.getId() != %me) { continue; }
      $Opt::PotionLast = getRealTime();
      UseHealingPotion(%i);
      echo("Opt auto-potion: HP " @ mFloor(%hp) @ "/" @ %mhp @ ", drank one, HP now " @ mFloor(MainCharacter.GetCurrentHealth()) @ ", " @ OptPotions() @ " left");
      OptRefreshNow();
      return;
   }
}

// POTIONS BY A CLICK (the player: "let's make it possible for potions that require being dropped
// in the portrait to work by double-clicking on them, dropping them in the portrait doesn't
// always work"; the game's inventory slots report no clicks to scripts, only drags, so he chose
// "click them in the window"). a bar above the tickboxes: one button per kind of such potion in
// your bag (Regeneration, Harden, Berserker, the bloods ...), with how many you have. not the
// Healing Potion (keys 1-4, Auto Potion) nor the Magic Pumpkin (only a Farmer can use it).
// rebuilt only when what is in the bag changed; hidden when there are none
function OptPotionBarUpdate()
{
   if (!isObject(OptPotionBar) || !isObject(MainCharacter)) { return; }
   %party = MainCharacter.getParty();
   if (!isObject(%party)) { return; }
   %inv = %party.GetInventory();
   if (!isObject(%inv)) { return; }
   %n = 0;
   %slots = %inv.GetNumSlots();
   for (%k = 0; %k < %slots; %k++)
   {
      %it = %inv.GetItem(%k);
      if (!isObject(%it)) { continue; }
      %idb = %it.getDataBlock();
      if (!%idb.Useable || %idb.category !$= "Potion" || %idb.Name $= "Healing_Potion" || %idb.Name $= "Magic_Pumpkin") { continue; }
      %nm = %idb.getName();
      %amt = %inv.GetItemAmount(%k);
      if (%amt < 1) { %amt = 1; }
      %at = -1;
      for (%j = 0; %j < %n && %at < 0; %j++) { if (%kind[%j] $= %nm) { %at = %j; } }
      if (%at < 0)
      {
         %at = %n;
         %kind[%n] = %nm;
         %label[%n] = %it.getNameString();
         %cnt[%n] = 0;
         %n++;
      }
      %cnt[%at] += %amt;
   }
   %sig = "";
   for (%j = 0; %j < %n; %j++) { %sig = %sig @ %kind[%j] @ ":" @ %cnt[%j] @ " "; }
   if (%sig $= $Opt::PotionSig) { return; }
   $Opt::PotionSig = %sig;
   for (%i = OptPotionBar.getCount() - 1; %i >= 0; %i--) { OptPotionBar.getObject(%i).delete(); }
   for (%j = 0; %j < %n; %j++)
   {
      %b = new GuiButtonCtrl()
      {
         Profile = "OptButtonProfile";
         HorizSizing = "right";
         VertSizing = "bottom";
         position = "6 4";
         Extent = "117 20";
         text = %label[%j] @ (%cnt[%j] > 1 ? " x" @ %cnt[%j] : "");
         Command = "OptUsePotionKind(" @ %kind[%j] @ ");";
      };
      OptPotionBar.add(%b);
   }
   $Opt::PotionN = %n;
   // placed again at once: the bar's height follows its rows
   $Opt::BarRect = "";
   if ($Opt::BarW !$= "") { OptPlaceBars($Opt::BarX, $Opt::BarY, $Opt::BarW); }
}

// a potion button was clicked: one of that kind from your bag is used on your lord the way the
// game does when it is dropped on the portrait (Creature::UseItem), then taken from the bag as
// UseHealingPotion takes a Healing Potion. an effect already running is not stacked by the game
// (AddSpellEffectProc returns 0), and its chance roll (100 + the item's level - your level,
// 2 to 95 %) can fail: then the potion stays in the bag. a blood's permanent stats
// (PermanentBuffProc) are added though its call returns nothing: it is taken. the buttons are
// rebuilt by the next fast tick, never inside this click
function OptUsePotionKind(%name)
{
   if (!isObject(MainCharacter) || MainCharacter.getState() $= "Dead") { return; }
   %inv = MainCharacter.getParty().GetInventory();
   %slots = %inv.GetNumSlots();
   for (%k = 0; %k < %slots; %k++)
   {
      %it = %inv.GetItem(%k);
      if (!isObject(%it) || %it.getDataBlock().getName() !$= %name) { continue; }
      %idb = %it.getDataBlock();
      %label = %it.getNameString();
      %used = MainCharacter.UseItem(%it);
      %perm = %idb.hitProc !$= "" && isObject(%idb.hitProc) && %idb.hitProc.class $= "PermanentBuffProc";
      if (%used || %perm)
      {
         %inv.RemoveItem(%k, 1);
         %inv.UpdateIcon(%k);
         sfxPlayOnce(ui_potion_down);
         echo("Opt potion: used " @ %label @ " on your lord");
      }
      else { echo("Opt potion: " @ %label @ " did nothing (the game's own chance roll failed, or its effect is already on you), kept"); }
      $Opt::PotionSig = "";
      return;
   }
}

function OptFastTick()
{
   cancel($Opt::FastSchedule);
   $Opt::FastSchedule = schedule($Opt::Fast, 0, OptFastTick);
   // milliseconds of play: real time while the game is not paused
   %rt = getRealTime();
   if ($Opt::PlayLast !$= "" && $timeScale != 0) { $Opt::PlayMs += mClamp(%rt - $Opt::PlayLast, 0, 1000); }
   $Opt::PlayLast = %rt;
   // A DRAG LEFT ON (the player: "the mod cannot be interacted anymore, I can't tick or untick the
   // boxes ... my hero is chasing an enemy miles away"). a mouse button let go outside the game
   // (alt-tab) never reaches the window: $Opt::Dragging stayed on, the window kept the mouse (no
   // box could be clicked) and this tick returned here before auto-attack, so nothing let go of
   // the chase. a drag with no movement for 2 s ends now, and the window lets go of the mouse
   // (taken off the screen and put back: a control that sleeps gives up the mouse)
   if ($Opt::Dragging && %rt - $Opt::DragLast > 2000) { OptEndDrag(); }
   // auto-attack is looked after whatever the window does (hidden with F8, being dragged)
   if (isObject(PlayGui) && isObject(Canvas) && Canvas.getContent() == PlayGui.getId() && isObject(MainCharacter))
   {
      %ta = getRealTime();
      if ($pref::Opt::AutoAttack) { OptAutoAttack(); } else { OptAutoAttackDrop(); }
      if ($pref::Opt::AutoPotion) { OptAutoPotion(); }
      OptPerfNote(getRealTime() - %ta, 0, "tick");
   }
   if (!$Opt::Visible || $Opt::Dragging || !isObject(OptOverlay) || !isObject(Canvas)) { return; }
   // the new game screen: each setting's ok / change follows your click at once, not at the
   // next 2 s refresh. redrawn only when the text changed
   if (isObject(createCharacterGui) && Canvas.getContent() == createCharacterGui.getId())
   {
      %st = OptSetupNow();
      %sb = OptSetupText();
      if (%st !$= $Opt::SetupTitle || %sb !$= $Opt::SetupBody)
      {
         $Opt::SetupTitle = %st;
         $Opt::SetupBody = %sb;
         OptAttach(createCharacterGui);
         OptShow("", %st, "", %sb);
      }
      return;
   }
   if (!isObject(PlayGui)) { return; }
   if (Canvas.getContent() != PlayGui.getId() || !isObject(MainCharacter) || $OptLastBody $= "") { return; }
   OptPotionBarUpdate();
   OptJunkBlink();
   // the potions in your bag changed any other way (one dragged onto a portrait, one picked up):
   // answered at once too
   %pn = OptPotions();
   if (%pn !$= $Opt::PotSeen)
   {
      $Opt::PotSeen = %pn;
      OptRefreshNow();
   }
   if ($OptLastTarget $= "" && $OptThenPos $= "" && strstr($OptLastBody, "@FLASH@") < 0) { return; }
   %t0 = getRealTime();
   OptShow($OptLastTarget, $OptLastTitle, $OptLastInfo, $OptLastBody);
   OptPerfNote(getRealTime() - %t0, 0, "redraw");
}

// ------------------------------------------------------------------ portals
// a portal site, once won, is yours, and the game builds its twin in town
// (Building::takeOverByPlayer). clicking either one moves your party to the other at once
// (Building::onFriendlySingleClicked, at most once every 1.2 s). so the town links to every
// portal you own, and one field portal leads to another through town.
// $OptPort*: entry spot and exit spot, both directions of every pair

function OptPortalScan()
{
   $OptPortN = 0;
   if (!isObject(TownBuildingsGroup)) { return; }
   for (%i = 0; %i < TownBuildingsGroup.getCount(); %i++)
   {
      %b = TownBuildingsGroup.getObject(%i);
      %t = %b.teleportTransform;
      if (%t $= "") { continue; }
      %p = %b.getPosition();
      // the exit is 2 m above the other portal's spot
      OptPortalAdd(%p, getWord(%t, 0) SPC getWord(%t, 1) SPC (getWord(%t, 2) - 2));
      OptPortalAdd(getWord(%t, 0) SPC getWord(%t, 1) SPC (getWord(%t, 2) - 2), %p);
   }
}

function OptPortalAdd(%in, %out)
{
   for (%k = 0; %k < $OptPortN; %k++)
   {
      if (mAbs($OptPortX[%k] - getWord(%in, 0)) < 3 && mAbs($OptPortY[%k] - getWord(%in, 1)) < 3) { return; }
   }
   $OptPortX[$OptPortN] = getWord(%in, 0);
   $OptPortY[$OptPortN] = getWord(%in, 1);
   $OptPortZ[$OptPortN] = getWord(%in, 2);
   $OptPortEX[$OptPortN] = getWord(%out, 0);
   $OptPortEY[$OptPortN] = getWord(%out, 1);
   $OptPortN++;
}

// the fastest way from %from to %to: on foot, or through one portal, or through two (field
// portal > town > another town portal). a portal is taken whenever it is shorter, by any
// distance (the player's rule: it walked 375 m past a portal that made it 350 m). sets
// $OptRouteVia (portals used, 0 = walk straight) and $OptRouteSave (metres saved); returns the
// spot to walk to now
function OptRoute(%from, %to)
{
   %fx = getWord(%from, 0);
   %fy = getWord(%from, 1);
   %tx = getWord(%to, 0);
   %ty = getWord(%to, 1);
   %direct = mSqrt((%tx - %fx) * (%tx - %fx) + (%ty - %fy) * (%ty - %fy));
   %best = %direct;
   %first = -1;
   $OptRouteVia = 0;
   for (%a = 0; %a < $OptPortN; %a++)
   {
      %dx = $OptPortX[%a] - %fx;
      %dy = $OptPortY[%a] - %fy;
      %da = mSqrt(%dx * %dx + %dy * %dy);
      if (%da >= %best) { continue; }
      %dx = %tx - $OptPortEX[%a];
      %dy = %ty - $OptPortEY[%a];
      %d1 = %da + mSqrt(%dx * %dx + %dy * %dy);
      if (%d1 < %best) { %best = %d1; %first = %a; %out = %a; $OptRouteVia = 1; }
      for (%b = 0; %b < $OptPortN; %b++)
      {
         if (%b == %a) { continue; }
         %dx = $OptPortX[%b] - $OptPortEX[%a];
         %dy = $OptPortY[%b] - $OptPortEY[%a];
         %mid = mSqrt(%dx * %dx + %dy * %dy);
         // the second portal must be near where the first lets you out (both in town)
         if (%mid > 150) { continue; }
         %dx = %tx - $OptPortEX[%b];
         %dy = %ty - $OptPortEY[%b];
         %d2 = %da + %mid + mSqrt(%dx * %dx + %dy * %dy);
         if (%d2 < %best) { %best = %d2; %first = %a; %out = %b; $OptRouteVia = 2; }
      }
   }
   if (%first < 0)
   {
      $OptRouteSave = 0;
      return %to;
   }
   $OptRouteSave = %direct - %best;
   // where the last portal of the way lets you out (OptHuntFrom: where the walk to a target begins)
   $OptRouteOutX = $OptPortEX[%out];
   $OptRouteOutY = $OptPortEY[%out];
   return $OptPortX[%first] SPC $OptPortY[%first] SPC $OptPortZ[%first];
}

// ------------------------------------------------------------------ small helpers

function OptMin(%a, %b) { return %a < %b ? %a : %b; }
function OptMax(%a, %b) { return %a > %b ? %a : %b; }
function OptPct(%x) { return mRound(%x * 100) @ "%"; }
function OptNum(%x) { return mFloatLength(%x, 1); }
function OptVis(%obj) { return isObject(%obj) && %obj.isVisible(); }
function OptShown(%raw) { return mFloor(%raw / 4) + 1; }

function OptDist2D(%a, %b)
{
   return VectorDist(getWord(%a, 0) SPC getWord(%a, 1) SPC 0, getWord(%b, 0) SPC getWord(%b, 1) SPC 0);
}

// 16-way screen direction (0 = up, clockwise). Asks the game view to project two ground
// points (the lord, and 2m from him toward the target) onto the screen, so it is right for
// any camera. If the view cannot project, falls back to the fixed isometric camera
// (IsoCameraData.Offset: the camera sits at lord + Offset looking back, so screen-up is -Offset).
function OptArrowIndex(%from, %to)
{
   %dx = getWord(%to, 0) - getWord(%from, 0);
   %dy = getWord(%to, 1) - getWord(%from, 1);
   if (mAbs(%dx) + mAbs(%dy) < 1) { return -1; }
   %len = mSqrt(%dx * %dx + %dy * %dy);
   %done = 0;
   if (!$Opt::NoProject && isObject(PlayGui))
   {
      %z = getWord(%from, 2);
      %p0 = PlayGui.project(getWord(%from, 0) SPC getWord(%from, 1) SPC %z);
      %p1 = PlayGui.project((getWord(%from, 0) + (2 * %dx) / %len) SPC (getWord(%from, 1) + (2 * %dy) / %len) SPC %z);
      if (getWordCount(%p0) >= 2 && getWordCount(%p1) >= 2)
      {
         %right = getWord(%p1, 0) - getWord(%p0, 0);
         %up = getWord(%p0, 1) - getWord(%p1, 1);
         %done = 1;
      }
      else
      {
         $Opt::NoProject = 1;
         echo("Hinterland Guide: PlayGui.project not available, using the fixed camera for the compass");
      }
   }
   if (!%done)
   {
      %ox = 4.5;
      %oy = -4.5;
      if (isObject(IsoCameraData) && getWordCount(IsoCameraData.Offset) >= 2)
      {
         %ox = getWord(IsoCameraData.Offset, 0);
         %oy = getWord(IsoCameraData.Offset, 1);
      }
      %ux = -%ox;
      %uy = -%oy;
      %up = %dx * %ux + %dy * %uy;
      %right = %dx * %uy - %dy * %ux;
   }
   if (mAbs(%up) + mAbs(%right) < 0.001) { return -1; }
   %ang = mRadToDeg(mAtan(%right, %up));
   if (%ang < 0) { %ang += 360; }
   return mRound(%ang / 22.5) % 16;
}

function OptDirWord(%idx)
{
   if (%idx < 0) { return "here"; }
   return getWord("up up-right right down-right down down-left left up-left", mRound(%idx / 2) % 8);
}

function OptWhere(%pos)
{
   %d = OptDist2D(MainCharacter.getPosition(), %pos);
   return OptDirWord(OptArrowIndex(MainCharacter.getPosition(), %pos)) @ ", " @ mRound(%d) @ "m";
}

// count of d101 rolls (0-100) below a threshold, as getRandom(100) < x in the game
function OptRollCount(%x)
{
   return mClamp(mCeil(%x), 0, 101);
}

function OptAtkCount(%isEnemy)
{
   %p = $gAttackStatPercent;
   if (%isEnemy) { %p += MainCharacter.enemyBonus; }
   return OptRollCount(%p);
}

function OptDefCount(%isEnemy)
{
   %p = $gDefenseStatPercent;
   if (%isEnemy) { %p += MainCharacter.enemyBonus; }
   return OptRollCount(%p);
}

// the game adds $Game::gBaseCriticalChance, which is never set, so base crit is 0
function OptCritCount(%bonus)
{
   return OptRollCount($Game::gBaseCriticalChance + %bonus);
}

// ------------------------------------------------------------------ exact swing math

// fills $OptB[tag, k] with P(Binomial(n, p) = k)
function OptBinom(%n, %p, %tag)
{
   for (%k = 0; %k <= %n; %k++) { $OptB[%tag, %k] = 0; }
   if (%n <= 0) { $OptB[%tag, 0] = 1; return; }
   if (%p <= 0) { $OptB[%tag, 0] = 1; return; }
   if (%p >= 1) { $OptB[%tag, %n] = 1; return; }
   %r = %p / (1 - %p);
   %m = mFloor((%n + 1) * %p);
   if (%m > %n) { %m = %n; }
   $OptB[%tag, %m] = 1;
   for (%k = %m; %k < %n; %k++) { $OptB[%tag, %k + 1] = $OptB[%tag, %k] * ((%n - %k) / (%k + 1)) * %r; }
   for (%k = %m; %k > 0; %k--) { $OptB[%tag, %k - 1] = $OptB[%tag, %k] * (%k / (%n - %k + 1)) / %r; }
   %s = 0;
   for (%k = 0; %k <= %n; %k++) { %s += $OptB[%tag, %k]; }
   for (%k = 0; %k <= %n; %k++) { $OptB[%tag, %k] = $OptB[%tag, %k] / %s; }
}

function OptCritHit(%amt, %abs, %floor, %base)
{
   %v = %amt - %abs;
   if (%v <= 0) { %v = %base; }
   if (%v < %floor) { %v = %floor; }
   if (%v > 999) { %v = 999; }
   return %v;
}

// expected damage of one swing, mirroring CalcCombat exactly (checked against 200,000 simulated
// swings per case, within 0.5%). what absorbs a hit: with $gCombatMethod 0 the defender rolls
// each defense point, and then the game ALSO runs `else if (getRandom(0,100) < $gBlockChance)
// absorb = totaldefense` (that else belongs to `if ($gCombatMethod == 1)`, so it runs for method
// 0 too): 50 times in 101 the hit meets the full defense, otherwise the rolled count.
// the mean is returned; the mean of the square goes to $OptMemoSq (see OptExpDmg2).
// NEVER name an array after another plus a digit: TorqueScript keeps $Name[i] as the plain
// variable "$Name" + "i", so the old $OptMemo2["2_8_71_70_0"] (attack 2 against defense 8) WAS
// $OptMemo["22_8_71_70_0"] (attack 22 against defense 8): every attack of 20-29 could be read
// as the square of a tiny one, and a raw-30 Gnoll looked SAFE to a level-3 lord
// worked out with running sums over the absorb counts, so a new matchup costs one pass over the
// attack rolls instead of attack rolls x absorb counts (same result as OptExpDmgSlow: within 4 in
// a million over 28,000 matchups at the game's settings)
function OptExpDmg(%A, %D, %atkCount, %defCount, %critCount)
{
   %key = %A @ "_" @ %D @ "_" @ %atkCount @ "_" @ %defCount @ "_" @ %critCount;
   %c = $OptMemo[%key];
   if (%c !$= "") { return %c; }
   %base = $gBaseDamageAmount;
   if (%base $= "") { %base = 1; }
   %na = OptMax(mCeil(%A), 0);
   %nd = OptMax(mCeil(%D), 0);
   %floor = %A * $gAlwaysHitPercent;
   %half = (%A + 1) / 2;
   // the sums assume no hit reaches the game's 999 cap; past it, the plain way
   if (%base <= 0 || 1.25 * OptMax(%na * %base, %half) >= 999 || %floor >= 999)
   {
      return OptExpDmgSlow(%A, %D, %atkCount, %defCount, %critCount);
   }
   OptBinom(%na, %atkCount / 101, "X");
   OptBinom(%nd, %defCount / 101, "Y");
   %q = OptRollCount($gBlockChance) / 101;
   %pc = %critCount / 101;
   // running sums over the rolled absorb count y (chance (1 - q) P(y)): chance, y and y^2
   %s0 = 0;
   %s1 = 0;
   %s2 = 0;
   for (%y = 0; %y <= %nd; %y++)
   {
      %p = (1 - %q) * $OptB["Y", %y];
      %s0 += %p;
      %s1 += %y * %p;
      %s2 += %y * %y * %p;
      $OptSumA[%y] = %s0;
      $OptSumB[%y] = %s1;
      $OptSumC[%y] = %s2;
   }
   %R = %s0;
   %lo = OptMax(%base, %floor);
   %m1 = 0;
   %m2 = 0;
   for (%x = 0; %x <= %na; %x++)
   {
      %px = $OptB["X", %x];
      if (%px < 0.0000001) { continue; }
      %hit = %x * %base;
      // no crit: absorb y up to k leaves hit - y, more leaves the always-hit share
      if (%x > 0 && %pc < 1)
      {
         %k = mFloor(%x - %floor / %base);
         if (%k > %nd) { %k = %nd; }
         %a0 = 0; %a1 = 0; %a2 = 0;
         if (%k >= 0) { %a0 = $OptSumA[%k]; %a1 = $OptSumB[%k]; %a2 = $OptSumC[%k]; }
         %e1 = %base * (%x * %a0 - %a1) + %floor * (%R - %a0);
         %e2 = %base * %base * (%x * %x * %a0 - 2 * %x * %a1 + %a2) + %floor * %floor * (%R - %a0);
         // the full block: the whole defense
         %v = %hit - %D;
         if (%v < %floor) { %v = %floor; }
         %w = %px * (1 - %pc);
         %m1 += %w * (%e1 + %q * %v);
         %m2 += %w * (%e2 + %q * %v * %v);
      }
      if (%pc > 0)
      {
         %camt = %hit;
         if (%camt < %half) { %camt = %half; }
         %camt *= 1.25;
         %w = %px * %pc / 101;
         %e1 = OptCritSum(%camt, %base, %floor, %lo, %R, %nd);
         %v = OptCritHit(%camt, %D, %floor, %base);
         %m1 += 35 * %w * (%e1 + %q * %v); %m2 += 35 * %w * ($OptCritE2 + %q * %v * %v);
         %e1 = OptCritSum(%camt, %base * 0.7, %floor, %lo, %R, %nd);
         %v = OptCritHit(%camt, %D * 0.7, %floor, %base);
         %m1 += 35 * %w * (%e1 + %q * %v); %m2 += 35 * %w * ($OptCritE2 + %q * %v * %v);
         %e1 = OptCritSum(%camt, %base * 0.5, %floor, %lo, %R, %nd);
         %v = OptCritHit(%camt, %D * 0.5, %floor, %base);
         %m1 += 15 * %w * (%e1 + %q * %v); %m2 += 15 * %w * ($OptCritE2 + %q * %v * %v);
         %e1 = OptCritSum(%camt, %base * 0.2, %floor, %lo, %R, %nd);
         %v = OptCritHit(%camt, %D * 0.2, %floor, %base);
         %m1 += 16 * %w * (%e1 + %q * %v); %m2 += 16 * %w * ($OptCritE2 + %q * %v * %v);
      }
   }
   $OptMemo[%key] = %m1;
   $OptMemoSq[%key] = %m2;
   return %m1;
}

// one kind of crit (absorb y x %fb): its mean over the rolled absorb counts, the mean of its
// square to $OptCritE2. y up to k1 leaves amt - fb y, up to k2 the always-hit share, more the
// game's minimum (OptCritHit)
function OptCritSum(%camt, %fb, %floor, %lo, %R, %nd)
{
   // the last y that leaves more than 0, tested the way OptCritHit tests it
   %k2 = mCeil(%camt / %fb) - 1;
   if (%camt - (%k2 + 1) * %fb > 0) { %k2++; }
   if (%k2 >= 0 && %camt - %k2 * %fb <= 0) { %k2--; }
   if (%k2 > %nd) { %k2 = %nd; }
   if (%k2 < -1) { %k2 = -1; }
   %k1 = %k2;
   if (%floor > 0)
   {
      %k1 = mFloor((%camt - %floor) / %fb + 0.000001);
      if (%k1 > %k2) { %k1 = %k2; }
      if (%k1 < -1) { %k1 = -1; }
   }
   %a0 = 0; %a1 = 0; %a2 = 0; %b0 = 0;
   if (%k1 >= 0) { %a0 = $OptSumA[%k1]; %a1 = $OptSumB[%k1]; %a2 = $OptSumC[%k1]; }
   if (%k2 >= 0) { %b0 = $OptSumA[%k2]; }
   $OptCritE2 = %camt * %camt * %a0 - 2 * %camt * %fb * %a1 + %fb * %fb * %a2 + %floor * %floor * (%b0 - %a0) + %lo * %lo * (%R - %b0);
   return %camt * %a0 - %fb * %a1 + %floor * (%b0 - %a0) + %lo * (%R - %b0);
}

// the same, one absorb count at a time (used past the 999 cap)
function OptExpDmgSlow(%A, %D, %atkCount, %defCount, %critCount)
{
   %key = %A @ "_" @ %D @ "_" @ %atkCount @ "_" @ %defCount @ "_" @ %critCount;
   %base = $gBaseDamageAmount;
   if (%base $= "") { %base = 1; }
   %na = OptMax(mCeil(%A), 0);
   %nd = OptMax(mCeil(%D), 0);
   OptBinom(%na, %atkCount / 101, "X");
   OptBinom(%nd, %defCount / 101, "Y");
   %floor = %A * $gAlwaysHitPercent;
   %q = OptRollCount($gBlockChance) / 101;
   %pc = %critCount / 101;
   %half = (%A + 1) / 2;
   %m1 = 0;
   %m2 = 0;
   for (%x = 0; %x <= %na; %x++)
   {
      %px = $OptB["X", %x];
      if (%px < 0.0000001) { continue; }
      %camt = %x * %base;
      if (%camt < %half) { %camt = %half; }
      %camt *= 1.25;
      // absorb outcome -1 is the full block, 0..nd the rolled count
      for (%y = -1; %y <= %nd; %y++)
      {
         if (%y < 0) { %py = %q; %ab = %D; }
         else { %py = (1 - %q) * $OptB["Y", %y]; %ab = %y * %base; }
         if (%py < 0.0000001) { continue; }
         // no crit: zero attack rolls is a miss, otherwise at least the always-hit share
         if (%x > 0 && %pc < 1)
         {
            %v = %x * %base - %ab;
            if (%v < %floor) { %v = %floor; }
            if (%v > 999) { %v = 999; }
            %w = %px * %py * (1 - %pc);
            %m1 += %w * %v;
            %m2 += %w * %v * %v;
         }
         if (%pc > 0)
         {
            %w = %px * %py * %pc / 101;
            %v = OptCritHit(%camt, %ab, %floor, %base);
            %m1 += 35 * %w * %v; %m2 += 35 * %w * %v * %v;
            %v = OptCritHit(%camt, %ab * 0.7, %floor, %base);
            %m1 += 35 * %w * %v; %m2 += 35 * %w * %v * %v;
            %v = OptCritHit(%camt, %ab * 0.5, %floor, %base);
            %m1 += 15 * %w * %v; %m2 += 15 * %w * %v * %v;
            %v = OptCritHit(%camt, %ab * 0.2, %floor, %base);
            %m1 += 16 * %w * %v; %m2 += 16 * %w * %v * %v;
         }
      }
   }
   $OptMemo[%key] = %m1;
   $OptMemoSq[%key] = %m2;
   return %m1;
}

// the mean of the squared damage of one swing (for how many whole swings a kill takes)
function OptExpDmg2(%A, %D, %atkCount, %defCount, %critCount)
{
   OptExpDmg(%A, %D, %atkCount, %defCount, %critCount);
   return $OptMemoSq[%A @ "_" @ %D @ "_" @ %atkCount @ "_" @ %defCount @ "_" @ %critCount];
}

// ------------------------------------------------------------------ units

// group "P" = your party, "E" = enemies
function OptSetUnit(%g, %k, %c)
{
   %t = %c.rateOfAttack;
   if (%t $= "" || %t <= 0) { %t = 1; }
   $OptA[%g, %k] = %c.getTotalAttack();
   $OptD[%g, %k] = %c.GetTotalDefense();
   $OptHP[%g, %k] = %c.GetCurrentHealth();
   $OptMHP[%g, %k] = %c.getMaxHealth();
   $OptT[%g, %k] = %t;
   $OptCB[%g, %k] = %c.criticalBonus;
   $OptNm[%g, %k] = OptCName(%c);
   $OptObj[%g, %k] = %c;
   $OptLv[%g, %k] = %c.Level;
   // on-hit effects of what it wears, worked out again only when its gear changes
   %id = %c.getId();
   %sig = %c.Inventory[0] @ "/" @ %c.Inventory[1] @ "/" @ %c.Inventory[2] @ "/" @ %c.Inventory[3];
   if ($OptFxSig[%id] !$= %sig)
   {
      OptUnitFx(%c, -1, "", 0);
      $OptFxSig[%id] = %sig;
      $OptFxHitOf[%id] = $OptFxTmpHit;
      $OptFxWornOf[%id] = $OptFxTmpWorn;
   }
   $OptFx[%g, %k] = $OptFxHitOf[%id];
   $OptFxW[%g, %k] = $OptFxWornOf[%id];
}

// a creature's name as shown in game: enemies have no personal name, the mouse-over shows their
// kind (Creature::onMouseOver: the datablock's DisplayName)
function OptCName(%c)
{
   %n = %c.getNameString();
   if (%n $= "") { %n = getStringTableEntry(%c.getDataBlock().DisplayName); }
   if (%n $= "") { %n = "enemy"; }
   return %n;
}

// he/him/his or she/her/her for a townsperson or visitor, by the datablock gender the game
// itself checks (Creature::getProfessionName)
function OptHe(%c) { return %c.getDataBlock().gender $= "Male" ? "he" : "she"; }
function OptHim(%c) { return %c.getDataBlock().gender $= "Male" ? "him" : "her"; }
function OptHis(%c) { return %c.getDataBlock().gender $= "Male" ? "his" : "her"; }

// a member of a raiding party (Building::spawnRaiders and TownRaider make the party with a raiderClass)
function OptIsRaider(%c)
{
   %p = %c.getParty();
   return isObject(%p) && %p.raiderClass !$= "";
}

function OptLoadParty()
{
   %party = MainCharacter.getParty();
   %n = %party.getPartySize();
   $OptN["P"] = 0;
   $OptLord = 0;
   for (%i = 0; %i < %n; %i++)
   {
      %c = %party.getMember(%i);
      if (!isObject(%c) || %c.getState() $= "Dead") { continue; }
      %k = $OptN["P"];
      OptSetUnit("P", %k, %c);
      if (%c.getId() == MainCharacter.getId()) { $OptLord = %k; }
      $OptN["P"]++;
   }
}

// stats a creature of this datablock gets at this level, per CreateCreature + AddLevel
// (level-ups 0,4,8.. health, 1,5.. attack, 2,6.. defense, 3,7.. random by HealthPerLevel/AttackPerLevel)
function OptDbStats(%db, %lvl)
{
   %N = %lvl + %db.LevelBonus;
   for (%k = 0; %k < 4; %k++) { %cnt[%k] = (%N > %k) ? mFloor((%N - 1 - %k) / 4) + 1 : 0; }
   %hpl = %db.HealthPerLevel;
   %apl = %db.AttackPerLevel;
   %ph = OptRollCount(%hpl) / 101;
   %pa = (OptRollCount(%hpl + %apl) - OptRollCount(%hpl)) / 101;
   %pd = 1 - %ph - %pa;
   %A = %db.Attack + %cnt[1] + %cnt[3] * %pa;
   %D = %db.Defense + %cnt[2] + %cnt[3] * %pd;
   %HP = %db.Health + 4 * %cnt[0] + 2 * %cnt[1] + 2 * %cnt[2] + %cnt[3] * (4 * %ph + 2 * %pa + 2 * %pd);
   %g = strreplace(getWord($Opt::GearTable, mClamp(%N, 0, getWordCount($Opt::GearTable) - 1)), "/", " ");
   %pw = OptRollCount(%N * 15 + 30 + %db.EquipWeaponModifier) / 101;
   %parm = OptRollCount(%N * 15 + 30) / 101;
   %A += %pw * getWord(%g, 0);
   %D += %parm * (getWord(%g, 1) + (50 / 101) * getWord(%g, 2));
   %T = %db.AttackSpeed;
   if (%T $= "" || %T <= 0) { %T = 1; }
   %mod = $gAttackRateModifier;
   if (%mod $= "") { %mod = 1; }
   $OptTmpA = %A;
   $OptTmpD = %D;
   $OptTmpHP = %HP;
   $OptTmpT = %T * %mod;
}

function OptSetStats(%set, %lvl)
{
   %a = 0; %d = 0; %h = 0; %t = 0; %n = 0;
   if (isObject(%set))
   {
      %n = %set.getCount();
      for (%i = 0; %i < %n; %i++)
      {
         OptDbStats(%set.getObject(%i), %lvl);
         %a += $OptTmpA; %d += $OptTmpD; %h += $OptTmpHP; %t += $OptTmpT;
      }
   }
   if (%n < 1) { %n = 1; %a = 1; %d = 0; %h = 3; %t = 1; }
   $OptSetA = %a / %n;
   $OptSetD = %d / %n;
   $OptSetHP = %h / %n;
   $OptSetT = %t / %n;
}

function OptMaxLevelBonus(%set)
{
   %m = 0;
   if (isObject(%set))
   {
      for (%i = 0; %i < %set.getCount(); %i++) { %m = OptMax(%m, %set.getObject(%i).LevelBonus); }
   }
   return %m;
}

function OptPutEstimate(%k, %a, %d, %h, %t, %name)
{
   $OptA["E", %k] = mRound(%a);
   $OptD["E", %k] = mRound(%d);
   $OptHP["E", %k] = %h;
   $OptMHP["E", %k] = %h;
   $OptT["E", %k] = %t;
   $OptCB["E", %k] = 0;
   $OptNm["E", %k] = %name;
   $OptObj["E", %k] = 0;
   // an estimate has no known gear effects; its level is taken as yours for effect chances
   $OptLv["E", %k] = $OptLv["P", $OptLord];
   $OptFx["E", %k] = "";
   $OptFxW["E", %k] = "";
}

// loads a site's enemies into group E. returns 1 if they are real (site scouted), 0 if estimated
function OptLoadSite(%e)
{
   $OptN["E"] = 0;
   if (%e.spawnPlaced && isObject(%e.memberSet))
   {
      %set = %e.memberSet;
      for (%i = 0; %i < %set.getCount(); %i++)
      {
         %c = %set.getObject(%i);
         if (!isObject(%c) || %c.getState() $= "Dead") { continue; }
         OptSetUnit("E", $OptN["E"], %c);
         $OptN["E"]++;
      }
      return 1;
   }
   // unscouted sites never change, so their estimate is worked out once
   %id = %e.getId() @ "_" @ MainCharacter.getId();
   %cn = $OptEstN[%id];
   if (%cn !$= "")
   {
      for (%k = 0; %k < %cn; %k++)
      {
         OptPutEstimate(%k, $OptEstA[%id, %k], $OptEstD[%id, %k], $OptEstHP[%id, %k], $OptEstT[%id, %k], $OptEstNm[%id, %k]);
      }
      $OptN["E"] = %cn;
      return 0;
   }
   OptEstimateSite(%e);
   for (%k = 0; %k < $OptN["E"]; %k++)
   {
      $OptEstA[%id, %k] = $OptA["E", %k];
      $OptEstD[%id, %k] = $OptD["E", %k];
      $OptEstHP[%id, %k] = $OptHP["E", %k];
      $OptEstT[%id, %k] = $OptT["E", %k];
      $OptEstNm[%id, %k] = $OptNm["E", %k];
   }
   $OptEstN[%id] = $OptN["E"];
   return 0;
}

// expected enemies of an unscouted site, following BasicEncounter::onPlaceInitialSpawn:
// average spawn count, levels spread evenly over the site's level range,
// leaders first while their level fits, runts or a 50/101 normal mix at low levels
function OptEstimateSite(%e)
{
   $OptN["E"] = 0;
   %ms = %e.MonsterSet;
   if (!isObject(%ms)) { return; }
   %lo = getWord(%e.MonsterLevelRange, 0);
   %hi = getWord(%e.MonsterLevelRange, 1);
   %cnt = mRound((getWord(%e.SpawnNumberRange, 0) + getWord(%e.SpawnNumberRange, 1)) / 2);
   %leaders = 0;
   for (%k = 0; %k < %cnt; %k++)
   {
      %lvl = mClamp(mRound(%lo + (%hi - %lo) * (%k + 0.5) / %cnt), 0, 999);
      if (inRange(%lvl, %ms.LeaderLevels) && (%leaders < %e.NumLeaders))
      {
         OptSetStats(%ms.Leaderset, %lvl);
         OptPutEstimate(%k, $OptSetA, $OptSetD, $OptSetHP, $OptSetT, "leader L" @ OptShown(%lvl));
         %leaders++;
      }
      else if (inRange(%lvl, %ms.runtLevels))
      {
         OptSetStats(%ms.RuntSet, %lvl);
         if (inRange(%lvl, %ms.normalLevels))
         {
            %ra = $OptSetA; %rd = $OptSetD; %rh = $OptSetHP; %rt = $OptSetT;
            OptSetStats(%ms.NormalSet, %lvl);
            %wn = 50 / 101;
            OptPutEstimate(%k, %wn * $OptSetA + (1 - %wn) * %ra, %wn * $OptSetD + (1 - %wn) * %rd, %wn * $OptSetHP + (1 - %wn) * %rh, %wn * $OptSetT + (1 - %wn) * %rt, "L" @ OptShown(%lvl));
         }
         else
         {
            OptPutEstimate(%k, $OptSetA, $OptSetD, $OptSetHP, $OptSetT, "runt L" @ OptShown(%lvl));
         }
      }
      else
      {
         OptSetStats(%ms.NormalSet, %lvl);
         OptPutEstimate(%k, $OptSetA, $OptSetD, $OptSetHP, $OptSetT, "L" @ OptShown(%lvl));
      }
   }
   $OptN["E"] = %cnt;
}

// the highest raw-level enemy a site has (or will spawn), loaded alone into group E.
// returns its raw level (the number CreatureData::onDisabled compares for David)
function OptLoadTopEnemy(%e)
{
   $OptN["E"] = 0;
   if (%e.spawnPlaced && isObject(%e.memberSet))
   {
      %best = 0;
      %bl = -1;
      for (%i = 0; %i < %e.memberSet.getCount(); %i++)
      {
         %c = %e.memberSet.getObject(%i);
         if (!isObject(%c) || %c.getState() $= "Dead") { continue; }
         if (%c.Level > %bl) { %bl = %c.Level; %best = %c; }
      }
      if (isObject(%best))
      {
         OptSetUnit("E", 0, %best);
         $OptN["E"] = 1;
      }
      return %bl;
   }
   %ms = %e.MonsterSet;
   if (!isObject(%ms)) { return -1; }
   // unscouted sites never change, so this estimate is worked out once
   %id = %e.getId() @ "_" @ MainCharacter.getId();
   if ($OptTopCacheN[%id] !$= "")
   {
      OptPutEstimate(0, $OptTopCacheA[%id], $OptTopCacheD[%id], $OptTopCacheHP[%id], $OptTopCacheT[%id], $OptTopCacheNm[%id]);
      $OptN["E"] = 1;
      return $OptTopCacheN[%id];
   }
   %hi = getWord(%e.MonsterLevelRange, 1);
   %set = %ms.NormalSet;
   if (inRange(%hi, %ms.LeaderLevels) && %e.NumLeaders > 0) { %set = %ms.Leaderset; }
   else if (inRange(%hi, %ms.runtLevels) && !inRange(%hi, %ms.normalLevels)) { %set = %ms.RuntSet; }
   OptSetStats(%set, %hi);
   %raw = %hi + OptMaxLevelBonus(%set);
   OptPutEstimate(0, $OptSetA, $OptSetD, $OptSetHP, $OptSetT, "its strongest enemy (raw " @ %raw @ ")");
   $OptN["E"] = 1;
   $OptTopCacheA[%id] = $OptA["E", 0];
   $OptTopCacheD[%id] = $OptD["E", 0];
   $OptTopCacheHP[%id] = $OptHP["E", 0];
   $OptTopCacheT[%id] = $OptT["E", 0];
   $OptTopCacheNm[%id] = $OptNm["E", 0];
   $OptTopCacheN[%id] = %raw;
   return %raw;
}

// ------------------------------------------------------------------ on-hit effects of gear
// CalcCombat calls checkProcs on every swing that lands. the effects (data/Items/ItemProcs.cs,
// SpellEffects/spellEffects.cs) fire with chance + (attacker level - defender level), kept
// between 2 and 95, in 100:
//   S  stun (AddSpellEffectProc, effect Stun): the target does nothing for the duration.
//      clubs, maces and hammers 5% for 1.9 s; the prefix Brutal the same
//   L  lightning (DirectDamageEffectProc): extra damage getRandom(0, 2 x hit + 2), on average
//      the hit + 1. prefixes Charged, suffix of Lightning, 5%
//   B  fire and poison (AddSpellEffectProc with a SpellEffect_RandomDamage): damage every tick
//      for the duration, not stacking. Flaming / of Fire 5%: 1-2 every 0.5 s for 5 s
//   Icy / of Ice add "Slow", which only changes a field (attackRate) nothing reads: no effect.
// an effect is four words: kind, base chance, value (stun seconds, or damage a second), duration.
// tested against simulated fights (model within the same margin as plain fights)

function OptProcWords(%proc)
{
   if (%proc $= "" || !isObject(%proc)) { return ""; }
   %cls = %proc.class;
   if (%cls $= "DirectDamageEffectProc") { return "L" SPC %proc.chance SPC "0 0"; }
   %eff = %proc.spellEffectToAdd;
   if (%cls !$= "AddSpellEffectProc" || !isObject(%eff)) { return ""; }
   if (%eff.effectType $= "Stun") { return "S" SPC %proc.chance SPC (%eff.duration / 1000) SPC "0"; }
   if (%eff.className $= "SpellEffect_RandomDamage" && %eff.tickRate > 0)
   {
      return "B" SPC %proc.chance SPC ((%eff.minAmount + %eff.maxAmount) / 2 * 1000 / %eff.tickRate) SPC (%eff.duration / 1000);
   }
   return "";
}

function OptFxJoin(%a, %b)
{
   if (%a $= "") { return %b; }
   if (%b $= "") { return %a; }
   return %a SPC %b;
}

// effects of one item (an item, or with %isDb its datablock only). %worn 0: what its hits do
// (the weapon's own hitProc and its prefix / suffix hitProc); %worn 1: what happens to whoever
// hits its wearer (prefix / suffix getHitProc, on armor)
function OptItemFx(%it, %isDb, %worn)
{
   %out = "";
   %idb = %isDb ? %it : %it.getDataBlock();
   if (!%worn && %idb.category $= "Weapon") { %out = OptProcWords(%idb.hitProc); }
   if (%isDb) { return %out; }
   %pm = %it.PrefixModifier;
   %sm = %it.SuffixModifier;
   if (isObject(%pm)) { %out = OptFxJoin(%out, OptProcWords(%worn ? %pm.getHitProc : %pm.hitProc)); }
   if (isObject(%sm)) { %out = OptFxJoin(%out, OptProcWords(%worn ? %sm.getHitProc : %sm.hitProc)); }
   return %out;
}

// effects of everything a creature wears, into $OptFxTmpHit (weapon slot) and $OptFxTmpWorn (all
// slots). with %slot 0-3 that slot is read as holding %item instead ("" = empty; %isDb: %item is
// a datablock), which is how a swap is scored
function OptUnitFx(%c, %slot, %item, %isDb)
{
   $OptFxTmpHit = "";
   $OptFxTmpWorn = "";
   for (%s = 0; %s < 4; %s++)
   {
      // a two-handed weapon scored next to a shield: the off-hand is empty (OptGearScore)
      if ($OptGDOffOn && %s == $OptGDOff) { continue; }
      if (%s == %slot) { %it = %item; %db = %isDb; }
      else { %it = OptEquipped(%c, %s); %db = 0; }
      if (%it $= "" || !isObject(%it)) { continue; }
      if (%s == 0) { $OptFxTmpHit = OptItemFx(%it, %db, 0); }
      $OptFxTmpWorn = OptFxJoin($OptFxTmpWorn, OptItemFx(%it, %db, 1));
   }
}

// chance that a swing lands at all: only a swing with no attack roll and no crit misses
function OptLandChance(%A, %atkCount, %critCount)
{
   %p = 1 - mPow(1 - %atkCount / 101, OptMax(mCeil(%A), 0)) * (1 - %critCount / 101);
   return %p > 0.01 ? %p : 0.01;
}

// effects as plain words, for the step text
function OptFxText(%fx)
{
   %t = "";
   for (%q = 0; %q < getWordCount(%fx); %q += 4)
   {
      %k = getWord(%fx, %q);
      %c = getWord(%fx, %q + 1) @ "% ";
      if (%k $= "S") { %w = %c @ "stun for " @ OptNum(getWord(%fx, %q + 2)) @ "s"; }
      else if (%k $= "L") { %w = %c @ "lightning (about double damage)"; }
      else { %w = %c @ "fire or poison (" @ OptNum(getWord(%fx, %q + 2) * getWord(%fx, %q + 3)) @ " damage)"; }
      %t = %t @ (%t $= "" ? "" : ", ") @ %w;
   }
   return %t;
}

// ------------------------------------------------------------------ fight

// returns "damageToParty damageIfAllOnLord fightLength partyHP"
function OptFight()
{
   %np = $OptN["P"];
   %ne = $OptN["E"];
   %hp = 0;
   for (%i = 0; %i < %np; %i++) { %hp += $OptHP["P", %i]; }
   $OptKillOrder = "";
   if (%ne < 1) { return "0 0 0" SPC %hp; }
   if (%np < 1) { return "inf inf inf 0"; }
   %pa = OptAtkCount(0);
   %pd = OptDefCount(0);
   %ea = OptAtkCount(1);
   %ed = OptDefCount(1);
   %L = $OptLord;
   // on-hit effects of worn gear (stun, lightning, fire, poison; see OptProcWords). most fights
   // have none, and then none of the effect code below runs
   %anyFx = 0;
   for (%i = 0; %i < %np && !%anyFx; %i++) { if ($OptFx["P", %i] !$= "" || $OptFxW["P", %i] !$= "") { %anyFx = 1; } }
   for (%j = 0; %j < %ne && !%anyFx; %j++) { if ($OptFx["E", %j] !$= "" || $OptFxW["E", %j] !$= "") { %anyFx = 1; } }
   // share of the time your party stands stunned by enemy weapons (every stunner counted as alive
   // for the whole fight, which keeps it on the safe side)
   %pst = 0;
   if (%anyFx)
   {
      for (%j = 0; %j < %ne; %j++)
      {
         %fx = $OptFx["E", %j];
         if (%fx $= "") { continue; }
         %ecc = OptCritCount($OptCB["E", %j]);
         %epl = OptLandChance($OptA["E", %j], %ea, %ecc);
         for (%q = 0; %q < getWordCount(%fx); %q += 4)
         {
            if (getWord(%fx, %q) !$= "S") { continue; }
            %lam = mClamp(getWord(%fx, %q + 1) + $OptLv["E", %j] - $OptLv["P", %L], 2, 95) / 100 * %epl / $OptT["E", %j];
            %val = getWord(%fx, %q + 2);
            %pst = 1 - (1 - %pst) * (1 - %val * %lam / (1 + %val * %lam));
         }
      }
      if (%pst > 0.8) { %pst = 0.8; }
   }
   for (%j = 0; %j < %ne; %j++)
   {
      // time to kill enemy j. kills take whole swings, so the last one overshoots: on average
      // by E[d^2] / (2 E[d]) (renewal estimate; within 10% of simulated fights, plain HP / DPS
      // is up to 45% short on quick kills), and never less than one swing
      %s = 0;
      %v = 0;
      %rate = 0;
      for (%i = 0; %i < %np; %i++)
      {
         %cc = OptCritCount($OptCB["P", %i]);
         %s += OptExpDmg($OptA["P", %i], $OptD["E", %j], %pa, %ed, %cc) / $OptT["P", %i];
         %v += OptExpDmg2($OptA["P", %i], $OptD["E", %j], %pa, %ed, %cc) / $OptT["P", %i];
         %rate += 1 / $OptT["P", %i];
      }
      if (%s <= 0) { return "inf inf inf" SPC %hp; }
      %over = %v / (2 * %s);
      %sx = %s;
      %stun = 0;
      %burnN = 0;
      %extraW = 0;
      %extraT = 0;
      if (%anyFx)
      {
         %ecc = OptCritCount($OptCB["E", %j]);
         %epl = OptLandChance($OptA["E", %j], %ea, %ecc);
         %efx = $OptFx["E", %j];
         %efw = $OptFxW["E", %j];
         for (%i = 0; %i < %np; %i++)
         {
            %fx = $OptFx["P", %i];
            %fw = $OptFxW["P", %i];
            if (%fx $= "" && %fw $= "" && %efx $= "" && %efw $= "") { continue; }
            // AddSpellEffectProc::onhit: chance + (attacker level - defender level), from 2 to 95
            %dl = $OptLv["P", %i] - $OptLv["E", %j];
            %cc = OptCritCount($OptCB["P", %i]);
            %mu = OptExpDmg($OptA["P", %i], $OptD["E", %j], %pa, %ed, %cc);
            %pl = OptLandChance($OptA["P", %i], %pa, %cc);
            %emu = OptExpDmg($OptA["E", %j], $OptD["P", %i], %ea, %pd, %ecc);
            // your weapon, on each hit that lands on enemy j
            for (%q = 0; %q < getWordCount(%fx); %q += 4)
            {
               %kind = getWord(%fx, %q);
               %lam = mClamp(getWord(%fx, %q + 1) + %dl, 2, 95) / 100 * %pl / $OptT["P", %i];
               %val = getWord(%fx, %q + 2);
               if (%kind $= "L") { %sx += %lam * (%mu / %pl + 1); }
               else if (%kind $= "S") { %stun = 1 - (1 - %stun) * (1 - %val * %lam / (1 + %val * %lam)); }
               else if (%kind $= "B") { %bLam[%burnN] = %lam; %bVal[%burnN] = %val; %bDur[%burnN] = getWord(%fx, %q + 3); %burnN++; }
            }
            // your armor, each time enemy j lands a hit on this member (it spreads its hits)
            for (%q = 0; %q < getWordCount(%fw); %q += 4)
            {
               %kind = getWord(%fw, %q);
               %lam = mClamp(getWord(%fw, %q + 1) + %dl, 2, 95) / 100 * %epl / $OptT["E", %j] / %np;
               %val = getWord(%fw, %q + 2);
               if (%kind $= "L") { %sx += %lam * (%emu / %epl); }
               else if (%kind $= "S") { %stun = 1 - (1 - %stun) * (1 - %val * %lam / (1 + %val * %lam)); }
               else if (%kind $= "B") { %bLam[%burnN] = %lam; %bVal[%burnN] = %val; %bDur[%burnN] = getWord(%fw, %q + 3); %burnN++; }
            }
            // enemy j's weapon, on each hit it lands on this member (its stuns are in %pst)
            for (%q = 0; %q < getWordCount(%efx); %q += 4)
            {
               %kind = getWord(%efx, %q);
               %lam = mClamp(getWord(%efx, %q + 1) - %dl, 2, 95) / 100 * %epl / $OptT["E", %j] / %np;
               %val = getWord(%efx, %q + 2);
               %dur = getWord(%efx, %q + 3);
               if (%kind $= "L") { %extraW += %lam * (%emu / %epl + 1); }
               else if (%kind $= "B") { %extraW += %val * %dur * %lam / (1 + %dur * %lam); }
            }
            // enemy j's armor, each time this member lands a hit on it
            for (%q = 0; %q < getWordCount(%efw); %q += 4)
            {
               %kind = getWord(%efw, %q);
               %lam = mClamp(getWord(%efw, %q + 1) - %dl, 2, 95) / 100 * %pl / $OptT["P", %i];
               %val = getWord(%efw, %q + 2);
               %dur = getWord(%efw, %q + 3);
               if (%kind $= "L") { %extraT += %lam * (%mu / %pl); }
               else if (%kind $= "B") { %extraT += %val * %dur * %lam / (1 + %dur * %lam); }
            }
         }
         %sx *= (1 - %pst);
      }
      %tk = OptMax(($OptHP["E", %j] + %over) / %sx, 1 / %rate);
      if (%burnN > 0)
      {
         // fire and poison don't stack and need time to start: over a kill of length t they add
         // rate x min(chance rate x t / 2, share of time burning)
         %sb = %sx;
         for (%q = 0; %q < %burnN; %q++) { %sb += %bVal[%q] * OptMin(%bLam[%q] * %tk / 2, %bDur[%q] * %bLam[%q] / (1 + %bDur[%q] * %bLam[%q])) * (1 - %pst); }
         %tk = OptMax(($OptHP["E", %j] + %over) / %sb, 1 / %rate);
      }
      %tau[%j] = %tk;
      %stunF[%j] = %stun;
      %xT[%j] = %extraT;
      %w = 0;
      for (%i = 0; %i < %np; %i++)
      {
         %w += OptExpDmg($OptA["E", %j], $OptD["P", %i], %ea, %pd, OptCritCount($OptCB["E", %j]));
      }
      %wp[%j] = (%w / %np) / $OptT["E", %j] + %extraW;
      %wl[%j] = OptExpDmg($OptA["E", %j], $OptD["P", %L], %ea, %pd, OptCritCount($OptCB["E", %j])) / $OptT["E", %j] + %extraW * %np;
      %ord[%j] = %j;
      %pri[%j] = %wp[%j] / %tau[%j];
   }
   for (%a = 0; %a < %ne; %a++)
   {
      %best = %a;
      for (%b = %a + 1; %b < %ne; %b++)
      {
         if (%pri[%ord[%b]] > %pri[%ord[%best]]) { %best = %b; }
      }
      %tmp = %ord[%a]; %ord[%a] = %ord[%best]; %ord[%best] = %tmp;
   }
   %t = 0; %dmg = 0; %dmgL = 0;
   for (%a = 0; %a < %ne; %a++)
   {
      %j = %ord[%a];
      %t += %tau[%j];
      // while you hit enemy j it stands stunned part of the time, and its armor may hit back
      %dmg += %wp[%j] * (%t - %tau[%j] * %stunF[%j]) + %xT[%j] * %tau[%j];
      %dmgL += %wl[%j] * (%t - %tau[%j] * %stunF[%j]) + %xT[%j] * %tau[%j];
      if (%a < 4) { $OptKillOrder = $OptKillOrder @ (%a ? " > " : "") @ $OptNm["E", %j]; }
   }
   if (%ne > 4) { $OptKillOrder = $OptKillOrder @ " > ..."; }
   return %dmg SPC %dmgL SPC %t SPC %hp;
}

// SAFE: half your party HP left and the lord survives even if every enemy hits only him
function OptRate(%res)
{
   %dmg = getWord(%res, 0);
   %hp = getWord(%res, 3);
   if (%dmg $= "inf" || %hp <= 0) { return "NO 0"; }
   %left = 1 - %dmg / %hp;
   %lordOk = getWord(%res, 1) < $OptHP["P", $OptLord];
   if (%left >= 0.5 && %lordOk) { return "SAFE" SPC %left; }
   if (%left >= 0.25) { return "OK" SPC %left; }
   if (%left > 0) { return "RISKY" SPC %left; }
   return "NO" SPC %left;
}

function OptRankOf(%r)
{
   if (%r $= "SAFE") { return 0; }
   if (%r $= "OK") { return 1; }
   if (%r $= "RISKY") { return 2; }
   return 3;
}

function OptRateColor(%r)
{
   if (%r $= "SAFE") { return $Opt::Green; }
   if (%r $= "OK") { return $Opt::Yellow; }
   if (%r $= "RISKY") { return $Opt::Orange; }
   return $Opt::Red;
}

// ------------------------------------------------------------------ sites

// where to walk for a site. encounterPosition is the corner of its 64 m cell (the map icon sits
// at +32 +32, spawn points 3-58 m in), so a scouted site points at its nearest living enemy
// and an unscouted one at the cell center. $OptSitePosName names that enemy, or ""
function OptSitePos(%e)
{
   $OptSitePosName = "";
   $OptSitePosObj = "";
   if (%e.spawnPlaced && isObject(%e.memberSet))
   {
      %lp = MainCharacter.getPosition();
      %best = "";
      %bd = 999999;
      %set = %e.memberSet;
      for (%i = 0; %i < %set.getCount(); %i++)
      {
         %c = %set.getObject(%i);
         if (!isObject(%c) || %c.getState() $= "Dead") { continue; }
         %d = OptDist2D(%lp, %c.getPosition());
         if (%d < %bd) { %bd = %d; %best = %c.getPosition(); %name = OptCName(%c); %bestObj = %c; }
      }
      if (%best !$= "") { $OptSitePosName = %name; $OptSitePosObj = %bestObj; return %best; }
   }
   %p = %e.encounterPosition;
   return (getWord(%p, 0) + 32) SPC (getWord(%p, 1) + 32) SPC 250.1;
}

function OptSiteName(%e)
{
   %n = %e.siteName;
   if (%n $= "" && isObject(%e.MonsterSet)) { %n = getStringTableEntry(%e.MonsterSet.creatureSiteName); }
   if (%n $= "") { %n = %e.getName(); }
   return strreplace(strreplace(%n, "<", ""), ">", "");
}

// rates one unwon site into slot %n of the site list
function OptScanOne(%e, %n, %lp)
{
   %exact = OptLoadSite(%e);
   %res = OptFight();
   %rate = OptRate(%res);
   $OptSite[%n] = %e;
   $OptSiteRate[%n] = getWord(%rate, 0);
   $OptSiteRank[%n] = OptRankOf($OptSiteRate[%n]);
   // a raider fort, except the strongest one while David is still open (its late raiders are the
   // last way to meet an enemy 20 raw levels above you)
   $OptSiteFort[%n] = (%e.canSpawn && %e.getId() != $OptKeepFort) ? 1 : 0;
   // that kept fort goes to the back of the list, behind every other site
   if (%e.getId() == $OptKeepFort) { $OptSiteRank[%n] = 4; }
   $OptSiteLeft[%n] = getWord(%rate, 1);
   $OptSiteDist[%n] = OptDist2D(%lp, OptSitePos(%e));
   $OptSiteExact[%n] = %exact;
   $OptSiteOrder[%n] = $OptKillOrder;
   $OptSiteCount[%n] = $OptN["E"];
   $OptSiteLvl[%n] = %e.GetAverageLevel();
   // the squares on the map: Encounter::getDangerLevel, 1 to 9
   $OptSiteDanger[%n] = %e.getDangerLevel();
   %top = OptLoadTopEnemy(%e);
   $OptSiteTop[%n] = %top;
   $OptSiteTopName[%n] = ($OptN["E"] > 0) ? $OptNm["E", 0] : "";
   $OptSiteTopHP[%n] = ($OptN["E"] > 0) ? $OptMHP["E", 0] : 0;
   $OptSiteTopD[%n] = ($OptN["E"] > 0) ? $OptD["E", 0] : 0;
   if (%top > $OptTopRaw) { $OptTopRaw = %top; $OptTopIdx = %n; }
}

// all sites in one go (used outside the pipeline)
function OptScanSites()
{
   OptLoadParty();
   OptKeepFortPick();
   %lp = MainCharacter.getPosition();
   %n = 0;
   $OptTopRaw = -1;
   $OptTopIdx = -1;
   $OptNearN = 0;
   for (%i = 0; %i < Encounters.getCount(); %i++)
   {
      %e = Encounters.getObject(%i);
      if (%e.won) { continue; }
      OptScanOne(%e, %n, %lp);
      OptSiteInsert(%n);
      %n++;
   }
   $OptSiteN = %n;
   OptSiteFinish();
}

// plan order: SAFE first, then OK, RISKY, NO; within a rating raider forts first (raids only
// come from forts still standing, and a fort always leaves a treasure chest), then the highest
// monster level (more XP and fame), then the nearest. true when site %x goes before site %y
function OptSiteBefore(%x, %y)
{
   if ($OptSiteRank[%x] != $OptSiteRank[%y]) { return $OptSiteRank[%x] < $OptSiteRank[%y]; }
   if ($OptSiteFort[%x] != $OptSiteFort[%y]) { return $OptSiteFort[%x] > $OptSiteFort[%y]; }
   if ($OptSiteLvl[%x] != $OptSiteLvl[%y]) { return $OptSiteLvl[%x] > $OptSiteLvl[%y]; }
   return $OptSiteDist[%x] < $OptSiteDist[%y];
}

// puts site %n (just rated; sites 0..n-1 are already in order) into the plan order and into
// the list of the five nearest. done one site at a time, so no frame sorts the whole list
function OptSiteInsert(%n)
{
   %pos = %n;
   while (%pos > 0 && OptSiteBefore(%n, $OptSiteIdx[%pos - 1]))
   {
      $OptSiteIdx[%pos] = $OptSiteIdx[%pos - 1];
      %pos--;
   }
   $OptSiteIdx[%pos] = %n;
   %m = $OptNearN;
   if (%m < 5) { $OptNearN++; }
   else if ($OptSiteDist[%n] >= $OptSiteDist[$OptNearIdx[4]]) { return; }
   else { %m = 4; }
   while (%m > 0 && $OptSiteDist[%n] < $OptSiteDist[$OptNearIdx[%m - 1]])
   {
      $OptNearIdx[%m] = $OptNearIdx[%m - 1];
      %m--;
   }
   $OptNearIdx[%m] = %n;
}

// the five nearest sites, used to score level-ups and abilities
function OptSiteFinish()
{
   $OptNearAllSafe = 1;
   for (%a = 0; %a < $OptNearN; %a++)
   {
      $OptNear[%a] = $OptSite[$OptNearIdx[%a]];
      if ($OptSiteRate[$OptNearIdx[%a]] !$= "SAFE") { $OptNearAllSafe = 0; }
   }
   OptLoadParty();
}

// total expected damage your party takes over the five nearest sites
function OptNearDamage()
{
   %sum = 0;
   for (%a = 0; %a < $OptNearN; %a++)
   {
      OptLoadSite($OptNear[%a]);
      %d = getWord(OptFight(), 0);
      if (%d $= "inf") { %d = 9999; }
      %sum += %d;
   }
   return %sum;
}

// ------------------------------------------------------------------ achievements

// owned: its rules are skipped. STEAM DECIDES whenever it answers (the player: Immortal showed as
// owned while Steam had it locked. the game had called its own unlock at the end of a won game,
// the guide had remembered that call in prefs and trusted it over Steam, but the unlock never
// reached Steam). asked many times per plan, so Steam is asked again at most once a minute; an
// unlock in this game counts at once (the UnlockAchievement hook) until Steam's next answer. the
// prefs' memory is used only when Steam can't be asked
function OptHave(%id)
{
   if (%id $= "") { return 0; }
   if (isFunction("steamGetAchievement"))
   {
      if ($Opt::HaveAsked[%id] !$= "" && getRealTime() - $Opt::HaveAsked[%id] < 60000) { return $Opt::HaveVal[%id] ? 1 : 0; }
      $Opt::HaveAsked[%id] = getRealTime();
      %r = steamGetAchievement(%id);
      %v = (%r $= "1" || %r $= "true" || %r $= "achieved" || %r $= "unlocked") ? 1 : 0;
      $Opt::HaveVal[%id] = %v;
      $pref::Opt::Have[%id] = %v;
      return %v;
   }
   return $pref::Opt::Have[%id] ? 1 : 0;
}

function OptNeed(%id)
{
   return !OptHave(%id);
}

$Opt::AchIds = "Hero Immortal Guardian Disrespectful Lone_Wolf David Wanderer Adventurer Champion";

function OptAchId(%name)
{
   return "$Achievement_" @ %name;
}

// names of the achievements you already own, comma separated
function OptOwnedList()
{
   %s = "";
   for (%i = 0; %i < getWordCount($Opt::AchIds); %i++)
   {
      %name = getWord($Opt::AchIds, %i);
      eval("%id = " @ OptAchId(%name) @ ";");
      if (OptHave(%id)) { %s = %s @ (%s $= "" ? "" : ", ") @ strreplace(%name, "_", " "); }
   }
   return %s;
}

// writes the Steam status of every achievement to console.log
function OptLogAchievements()
{
   if (!isFunction("steamGetAchievement")) { echo("Guide: steamGetAchievement is missing"); return; }
   for (%i = 0; %i < getWordCount($Opt::AchIds); %i++)
   {
      %name = getWord($Opt::AchIds, %i);
      eval("%id = " @ OptAchId(%name) @ ";");
      echo("Guide: " @ %name @ " (" @ %id @ ") steam='" @ steamGetAchievement(%id) @ "' have=" @ OptHave(%id));
   }
   echo("Guide: owned: " @ OptOwnedList());
}

// Immortal, Guardian, Disrespectful and Lone Wolf are only checked on a won Long game,
// Hard or Hardcore, raiders on (deleteGameFilesAndShowScore)
function OptRunOk()
{
   return MainCharacter.raidersEnabled && MainCharacter.DifficultyLevel >= 2 && MainCharacter.gameLength >= 2;
}

// any of the four run achievements still missing
function OptNeedRun()
{
   return OptNeed($Achievement_Immortal) || OptNeed($Achievement_Guardian) || OptNeed($Achievement_Disrespectful) || OptNeed($Achievement_Lone_Wolf);
}

// rules that still apply in this game
function OptNeedGuardian() { return OptRunOk() && OptNeed($Achievement_Guardian) && !MainCharacter.followerDied; }
function OptNeedLoneWolf() { return OptRunOk() && OptNeed($Achievement_Lone_Wolf) && !MainCharacter.partymemberJoined; }
function OptNeedImmortal() { return OptRunOk() && OptNeed($Achievement_Immortal) && !MainCharacter.hasDied; }
function OptNeedDisrespectful() { return OptRunOk() && MainCharacter.requestsEnabled && OptNeed($Achievement_Disrespectful) && !MainCharacter.fulfilledRequest; }

function OptAchLine(%label, %id, %state)
{
   if (OptHave(%id)) { return "  " @ %label @ ": " @ $Opt::Green @ "have" @ $Opt::White @ "\n"; }
   return "  " @ %label @ ": " @ %state @ $Opt::White @ "\n";
}

// what still has to happen before winning ends the run
function OptMissing()
{
   %m = "";
   if (MainCharacter.Level < $Achievement_Completion_Champion_Level && !OptHave($Achievement_Champion))
   {
      %m = "level " @ OptShown($Achievement_Completion_Champion_Level) @ " (Champion)";
   }
   if (!OptHave($Achievement_David) && MainCharacter.DifficultyLevel >= 2)
   {
      %m = %m @ (%m $= "" ? "" : ", ") @ "David";
   }
   return %m;
}

function OptAchText()
{
   %run = OptRunOk();
   %t = $Opt::Gold @ "ACHIEVEMENTS" @ $Opt::White @ "\n";
   %t = %t @ OptAchLine("Hero", $Achievement_Hero, $Opt::Yellow @ "win (" @ $OptSiteN @ " sites left)");
   if (!%run)
   {
      %no = $Opt::Grey @ "not this run";
      %t = %t @ OptAchLine("Immortal", $Achievement_Immortal, %no);
      %t = %t @ OptAchLine("Guardian", $Achievement_Guardian, %no);
      %t = %t @ OptAchLine("Disrespectful", $Achievement_Disrespectful, %no);
      %t = %t @ OptAchLine("Lone Wolf", $Achievement_Lone_Wolf, %no);
   }
   else
   {
      %t = %t @ OptAchLine("Immortal", $Achievement_Immortal, MainCharacter.hasDied ? $Opt::Red @ "lost" : $Opt::Yellow @ "on track");
      %t = %t @ OptAchLine("Guardian", $Achievement_Guardian, MainCharacter.followerDied ? $Opt::Red @ "lost" : $Opt::Yellow @ "on track");
      if (!MainCharacter.requestsEnabled) { %d = $Opt::Grey @ "not this run"; }
      else if (MainCharacter.fulfilledRequest) { %d = $Opt::Red @ "lost"; }
      else { %d = $Opt::Yellow @ "on track"; }
      %t = %t @ OptAchLine("Disrespectful", $Achievement_Disrespectful, %d);
      %t = %t @ OptAchLine("Lone Wolf", $Achievement_Lone_Wolf, MainCharacter.partymemberJoined ? $Opt::Red @ "lost" : $Opt::Yellow @ "on track");
   }
   if (MainCharacter.DifficultyLevel < 2) { %d = $Opt::Grey @ "not this run"; }
   else if ($OptTopIdx >= 0 && MainCharacter.Level <= $OptTopRaw - 20) { %d = $Opt::Yellow @ "open while you are raw " @ ($OptTopRaw - 20) @ " or less"; }
   else { %d = $Opt::Orange @ "only via late raiders now"; }
   %t = %t @ OptAchLine("David", $Achievement_David, %d);
   %lv = " (you " @ OptShown(MainCharacter.Level) @ ")";
   %t = %t @ OptAchLine("Wanderer", $Achievement_Wanderer, $Opt::Yellow @ "level " @ OptShown($Achievement_Completion_Wanderer_Level) @ %lv);
   %t = %t @ OptAchLine("Adventurer", $Achievement_Adventurer, $Opt::Yellow @ "level " @ OptShown($Achievement_Completion_Adventurer_Level) @ %lv);
   %t = %t @ OptAchLine("Champion", $Achievement_Champion, $Opt::Yellow @ "level " @ OptShown($Achievement_Completion_Champion_Level) @ %lv);
   return %t;
}

// ------------------------------------------------------------------ level-up advice

// returns "pick|details"
function OptLevelUpPick()
{
   // Creature::GainAttackLevel: attack +1, max HP +2. GainDefenseLevel: defense +1, max HP +2.
   // GainHealthLevel: max HP +4. decided over every enemy left on the map, like your gear (the
   // player's rule: what is better for the whole game, never the next few sites): the share of
   // your full health bar that killing each enemy alone costs (OptWGShare), lowest total wins.
   // worked out once per window: the answer is kept while your numbers and the list stand
   OptWGListNow();
   %n = $OptWGEnemies;
   if ($OptWGGen $= "" || %n < 1) { return "Attack|no enemies left to compare against"; }
   %L = $OptLord;
   %key = $OptWGSig @ "_" @ $OptWGFN @ "_" @ %n;
   if (%key $= $OptLvlKey) { return $OptLvlRes; }
   %a0 = $OptA["P", %L]; %d0 = $OptD["P", %L]; %h0 = $OptHP["P", %L]; %mh = $OptMHP["P", %L];
   $OptHP["P", %L] = %mh;
   %base = OptWGShare();
   $OptA["P", %L] = %a0 + 1; $OptHP["P", %L] = %mh + 2;
   %sA = OptWGShare();
   $OptA["P", %L] = %a0; $OptD["P", %L] = %d0 + 1;
   %sD = OptWGShare();
   $OptD["P", %L] = %d0; $OptHP["P", %L] = %mh + 4;
   %sH = OptWGShare();
   $OptHP["P", %L] = %h0;
   %pick = "Attack"; %best = %sA;
   if (%sD < %best) { %pick = "Defense"; %best = %sD; }
   if (%sH < %best) { %pick = "Health"; %best = %sH; }
   $OptLvlKey = %key;
   $OptLvlRes = %pick @ "|what one fight costs of your health bar, on average over the " @ %n @ " enemies left: now " @ OptNum(100 * %base / %n) @ "%, with Attack " @ OptNum(100 * %sA / %n) @ "%, Defense " @ OptNum(100 * %sD / %n) @ "%, Health " @ OptNum(100 * %sH / %n) @ "%";
   return $OptLvlRes;
}

function OptAbilityId(%cmd)
{
   %s = strstr(%cmd, "AddAbility(");
   if (%s < 0) { return ""; }
   %rest = getSubStr(%cmd, %s + 11, 80);
   %e = strstr(%rest, ")");
   if (%e < 0) { return ""; }
   return getSubStr(%rest, 0, %e);
}

// buildings that are working now, by what they make
function OptCountProducers(%kind)
{
   %n = 0;
   for (%i = 0; %i < TownBuildingsGroup.getCount(); %i++)
   {
      %b = TownBuildingsGroup.getObject(%i);
      %db = %b.getDataBlock();
      if (%kind $= "food" && %db.Food_Produced && %b.isCapableOfWork()) { %n++; }
      if (%kind $= "gold" && %db.Gold_Produced && %b.isCapableOfWork()) { %n++; }
   }
   return %n;
}

// returns "kind value text": kind C = combat (value = damage change), S = score points per day
function OptAbilityValue(%id)
{
   %L = $OptLord;
   %cyc = $gDayApparentCycle;
   %a0 = $OptA["P", %L]; %d0 = $OptD["P", %L]; %h0 = $OptHP["P", %L]; %t0 = $OptT["P", %L];
   %combat = 1;
   if (%id $= "dbAbilityWarrior") { $OptA["P", %L] = %a0 + 1; }
   else if (%id $= "dbAbilityGreatWarrior") { $OptA["P", %L] = %a0 + 2; }
   else if (%id $= "dbAbilityQuickAttack") { $OptT["P", %L] = mClamp(MainCharacter.realRateOfAttack - 0.07, 0.2, 4); }
   else if (%id $= "dbAbilityLightningSpeed") { $OptT["P", %L] = mClamp(MainCharacter.realRateOfAttack - 0.08, 0.2, 4); }
   else if (%id $= "dbAbilityDefender") { $OptD["P", %L] = %d0 + 1; }
   else if (%id $= "dbAbilityHealthy") { $OptHP["P", %L] = %h0 + 4; }
   else if (%id $= "dbAbilityVeryHealthy") { $OptHP["P", %L] = %h0 + 8; }
   else { %combat = 0; }
   if (%combat)
   {
      // over every enemy left, like the level-up pick: what one fight costs of your health bar
      %sh = OptWGShare();
      %d = %sh - $OptBaseShare;
      $OptA["P", %L] = %a0; $OptD["P", %L] = %d0; $OptHP["P", %L] = %h0; $OptT["P", %L] = %t0;
      %en = $OptWGEnemies;
      return "C" SPC %d SPC "one fight then costs " @ OptNum(100 * %sh / %en) @ "% of your health bar, now " @ OptNum(100 * $OptBaseShare / %en) @ "% (average over the " @ %en @ " enemies left)";
   }
   if (%id $= "dbTownAbilityGatherer") { %p = 0.2 * %cyc / 20; return "S" SPC %p SPC "+" @ OptNum(0.2 * %cyc) @ " food/day"; }
   if (%id $= "dbTownAbilityTrader") { %p = 0.2 * %cyc / 150; return "S" SPC %p SPC "+" @ OptNum(0.2 * %cyc) @ " gold/day"; }
   if (%id $= "dbTownAbilityBountiful") { %f = 0.1 * OptCountProducers("food") * %cyc; %p = %f / 20; return "S" SPC %p SPC "+" @ OptNum(%f) @ " food/day now"; }
   if (%id $= "dbTownAbilityMerchant") { %g = (0.2 - gTown.goldProductionBonus) * OptCountProducers("gold") * %cyc; %p = %g / 150; return "S" SPC %p SPC OptNum(%g) @ " gold/day now (sets the gold bonus to 0.2)"; }
   if (%id $= "dbTownAbilityFamous") { return "S 0" SPC "+10 fame now"; }
   if (%id $= "dbTownAbilityEngineer") { return "S 0" SPC "build cost x0.9 (sets it, now x" @ OptNum(gTown.BuildCostMultiplier) @ ")"; }
   if (%id $= "dbTownAbilityFavored") { return "S 0" SPC "favor x1.2 (sets it, now x" @ OptNum(gTown.FavorMultiplier) @ ")"; }
   if (%id $= "dbTownAbilityBookWorm") { return "S 0" SPC "research +0.2 per tick"; }
   if (%id $= "dbTownAbilityCraftsman") { return "S 0" SPC "item making +0.2 per tick"; }
   if (%id $= "dbTownAbilityHealer") { return "S 0" SPC "town heal +2"; }
   if (%id $= "dbTownAbilityGreatHealer") { return "S 0" SPC "town heal +4"; }
   if (%id $= "dbAbilityRegenerate") { return "S 0" SPC "lord heal +6"; }
   if (%id $= "dbAbilityMiser") { return "S 0" SPC "lord gold bonus +1"; }
   if (%id $= "dbAbilityScavenger") { return "S 0" SPC "lord drop bonus +5"; }
   if (%id $= "dbAbilityInheritance") { return "S 0" SPC "one random item near your level"; }
   return "S 0" SPC %id;
}

// returns "pick|details"
function OptAbilityPick()
{
   // combat abilities are scored at full HP over every enemy left (OptWGShare); your real HP is
   // put back before returning
   OptWGListNow();
   if ($OptWGGen $= "" || $OptWGEnemies < 1) { return "any|no enemies left to compare against"; }
   %L = $OptLord;
   %hpNow = $OptHP["P", %L];
   $OptHP["P", %L] = $OptMHP["P", %L];
   $OptBaseShare = OptWGShare();
   %bestC = -1; %bestCv = 0; %bestS = -1; %bestSv = -1; %gath = -1; %famous = -1;
   %lines = "";
   for (%i = 0; %i < 3; %i++)
   {
      %id = OptAbilityId($gAbilityButton[%i].Command);
      if (%id $= "") { continue; }
      %v = OptAbilityValue(%id);
      %name = getStringTableEntry(%id.Display);
      if (%name $= "") { %name = %id; }
      %abName[%i] = %name;
      %lines = %lines @ (%lines $= "" ? "" : "\n") @ %name @ ": " @ getWords(%v, 2);
      if (%id $= "dbTownAbilityGatherer") { %gath = %i; }
      if (%id $= "dbTownAbilityFamous") { %famous = %i; }
      if (getWord(%v, 0) $= "C" && (getWord(%v, 1) < %bestCv)) { %bestC = %i; %bestCv = getWord(%v, 1); }
      if (getWord(%v, 0) $= "S" && (getWord(%v, 1) > %bestSv)) { %bestS = %i; %bestSv = getWord(%v, 1); }
   }
   // 1. Gatherer while you have no food income: +1 food/day is exactly what a lone lord eats,
   //    so you never have to hire anyone (and then no townsperson can ever die)
   // 2. combat while any nearby site is not SAFE
   // 3. Famous (+10 fame) when fame is low and requests are on: each refusal costs 35 to 60
   // 4. the best economy ability, else the best combat one
   %pick = -1;
   %why = "";
   if (%gath >= 0 && OptFoodNet() <= 0 && gTown.FoodBonusPerDay <= 0) { %pick = %gath; %why = "+1 food a day, forever: exactly what you eat"; }
   else if (!$OptNearAllSafe && %bestC >= 0) { %pick = %bestC; %why = "nearby sites are not SAFE yet"; }
   else if (%famous >= 0 && OptNeedDisrespectful() && gTown.Fame < 50) { %pick = %famous; %why = "fame buffer for refusing requests"; }
   else if (%bestS >= 0 && %bestSv > 0) { %pick = %bestS; }
   else if (%bestC >= 0) { %pick = %bestC; }
   else if (%bestS >= 0) { %pick = %bestS; }
   $OptHP["P", %L] = %hpNow;
   return (%pick >= 0 ? %abName[%pick] : "any") @ "|" @ (%why $= "" ? "" : %why @ "\n") @ %lines;
}

// ------------------------------------------------------------------ gear advice

// slot an item would go in for this creature, same test as Creature::EquipWithItem
function OptItemSlot(%c, %itemData)
{
   if (!isFunction("GetCanPlaceItemInSlot")) { return ""; }
   %cdb = %c.getDataBlock();
   for (%s = 0; %s < 4; %s++)
   {
      if (GetCanPlaceItemInSlot(%cdb.InventorySlotType[%s], %itemData.ItemSlot)) { return %s; }
   }
   return "";
}

// the class speed bonus a weapon brings (applyItemDependentBonuses): one-hand / two-hand melee or missile
function OptWeaponBonus(%cdb, %idb)
{
   if (%idb.category !$= "Weapon") { return 0; }
   if (%idb.ActionType $= "missile attack") { return %cdb.MissileSpeedBonus + 0; }
   if (%idb.ActionType $= "melee attack")
   {
      if (isObject(%idb.ItemImage) && %idb.ItemImage.MountPoint == 4) { return %cdb.TwoHandMeleeSpeedBonus + 0; }
      return %cdb.OneHandMeleeSpeedBonus + 0;
   }
   return 0;
}

// the item worn in a slot, read only the way the game's own scripts do (Creature::getEquipment:
// EquipWithItem stores the item's name in Inventory[slot], unequipping clears it). never the
// engine's getInventoryItem: right after an item is taken off it can point at a deleted item,
// and reading that crashes the game
function OptEquipped(%c, %slot)
{
   %x = %c.Inventory[%slot];
   if (%x !$= "" && isObject(%x)) { return %x.getId(); }
   return "";
}

// a two-handed weapon: its image mounts at point 4, the game's own two-hand test
// (Creature::applyItemDependentBonuses)
function OptTwoHanded(%idb)
{
   return isObject(%idb.ItemImage) && %idb.ItemImage.MountPoint == 4;
}

// 1 when the game refuses %idb in slot %s of %c because of the other hand: no off-hand item
// (slot 2) next to a two-handed weapon (slot 0), and no two-handed weapon next to an off-hand item
function OptHandsBlocked(%c, %idb, %s)
{
   if (%s == 2)
   {
      %w = OptEquipped(%c, 0);
      return isObject(%w) && OptTwoHanded(%w.getDataBlock());
   }
   if (%s == 0 && OptTwoHanded(%idb)) { return isObject(OptEquipped(%c, 2)); }
   return 0;
}

// TWO-HANDED WEAPONS AGAINST YOUR WEAPON AND SHIELD TOGETHER (the player: "test two-handed weapons
// against your sword and shield together, and sell the ones that lose", by HP lost like all gear).
// the game refuses a two-handed weapon while the off-hand holds an item (displayOffHandError), so
// it was never scored and never sold. now it is scored as it would be worn: the weapon in slot 0
// AND the off-hand empty. 1 for such an item
function OptOffFor2H(%c, %idb, %slot)
{
   return %slot == 0 && OptTwoHanded(%idb) && isObject(OptEquipped(%c, 2));
}

// %it in %slot of party member %k, scored with the fight loaded (OptGearDelta: $OptGDNew), the
// off-hand item taken off too for a two-handed weapon next to it (OptOffFor2H)
function OptGearScore(%k, %c, %slot, %cur, %it)
{
   %cdb = %c.getDataBlock();
   %idb = %it.getDataBlock();
   %ia = %it.getAttack();
   %idf = %it.getDefense();
   %ih = %it.getHealth();
   %ic = %it.getCriticalBonus();
   %is = %it.getSpeed() + OptWeaponBonus(%cdb, %idb);
   if (OptOffFor2H(%c, %idb, %slot))
   {
      %off = OptEquipped(%c, 2);
      %ia -= %off.getAttack();
      %idf -= %off.getDefense();
      %ih -= %off.getHealth();
      %ic -= %off.getCriticalBonus();
      %is -= %off.getSpeed() + OptWeaponBonus(%cdb, %off.getDataBlock());
      $OptGDOff = 2;
      $OptGDOffOn = 1;
   }
   OptGDSwap(%it, 0);
   %d = OptGearDelta(%k, %c, %slot, %cur, %ia, %idf, %ih, %is, %ic);
   $OptGDOffOn = 0;
   return %d;
}

function OptTryItem(%k, %c, %it, %src)
{
   %idb = %it.getDataBlock();
   %cat = %idb.category;
   // any item that fits one of the four slots counts: weapons, armor, shields, helms and the
   // rings, amulets, boots and sandals (category Misc) that go in the Artifact slot.
   // heal wands and staffs cast on allies; they are not a weapon for the fight model
   if (%cat $= "Weapon" && %idb.ActionType !$= "melee attack" && %idb.ActionType !$= "missile attack") { return; }
   if (%idb.Useable) { return; }
   %slot = OptItemSlot(%c, %idb);
   if (%slot $= "" || OptHandsBlocked(%c, %idb, %slot)) { return; }
   %cur = OptEquipped(%c, %slot);
   if (isObject(%cur) && %cur.getId() == %it.getId()) { return; }
   %spd = %it.getSpeed() + OptWeaponBonus(%c.getDataBlock(), %idb);
   OptGDSwap(%it, 0);
   OptGearEval(%k, %c, %slot, %cur, %it.getNameString(), %src, %it.getAttack(), %it.getDefense(), %it.getHealth(), %spd, %it.getCriticalBonus(), 0);
   // what this item would do for you at the gear site, kept for the junk list (OptJunkAll)
   if (%k == $OptLord)
   {
      $OptItemD[%it.getId()] = $OptGDNew - $OptGearBase;
      $OptItemDGen[%it.getId()] = $OptGearGen;
   }
}

// tells the next OptGearDelta what the slot will hold, so its on-hit effects are scored too:
// an item, a datablock (%isDb 1), or "" for an emptied slot
function OptGDSwap(%item, %isDb)
{
   $OptGDHas = 1;
   $OptGDItem = %item;
   $OptGDIsDb = %isDb;
}

// change in damage taken at the loaded site if slot %slot of party member %k held an item with
// these stats instead of %cur. also sets $OptGDNew (the damage) and $OptGDWhy (the numbers)
function OptGearDelta(%k, %c, %slot, %cur, %ia, %idf, %ih, %is, %ic)
{
   %cdb = %c.getDataBlock();
   %ca = 0; %cd = 0; %ch = 0; %cs = 0; %cc = 0;
   if (isObject(%cur))
   {
      %ca = %cur.getAttack(); %cd = %cur.getDefense(); %ch = %cur.getHealth(); %cc = %cur.getCriticalBonus();
      %cs = %cur.getSpeed() + OptWeaponBonus(%cdb, %cur.getDataBlock());
   }
   %a0 = $OptA["P", %k]; %d0 = $OptD["P", %k]; %h0 = $OptHP["P", %k]; %t0 = $OptT["P", %k]; %b0 = $OptCB["P", %k];
   %a1 = %a0 + %ia - %ca;
   %d1 = %d0 + %idf - %cd;
   %t1 = mClamp(%t0 + %is - %cs, 0.2, 4);
   $OptA["P", %k] = %a1;
   $OptD["P", %k] = %d1;
   $OptHP["P", %k] = %h0 + %ih - %ch;
   $OptT["P", %k] = %t1;
   $OptCB["P", %k] = %b0 + %ic - %cc;
   // the on-hit effects change with the item too (OptGDSwap says what goes in the slot)
   %fh0 = $OptFx["P", %k];
   %fw0 = $OptFxW["P", %k];
   $OptGDFx = "";
   if ($OptGDHas)
   {
      OptUnitFx(%c, %slot, $OptGDItem, $OptGDIsDb);
      $OptFx["P", %k] = $OptFxTmpHit;
      $OptFxW["P", %k] = $OptFxTmpWorn;
      if ($OptGDItem !$= "" && isObject($OptGDItem)) { $OptGDFx = OptFxJoin(OptItemFx($OptGDItem, $OptGDIsDb, 0), OptItemFx($OptGDItem, $OptGDIsDb, 1)); }
      $OptGDHas = 0;
   }
   %new = getWord(OptFight(), 0);
   if (%new $= "inf") { %new = 9999; }
   $OptA["P", %k] = %a0; $OptD["P", %k] = %d0; $OptHP["P", %k] = %h0; $OptT["P", %k] = %t0; $OptCB["P", %k] = %b0;
   $OptFx["P", %k] = %fh0;
   $OptFxW["P", %k] = %fw0;
   $OptGDNew = %new;
   $OptGDWhy = "attack " @ %a0 @ ">" @ %a1 @ " defense " @ %d0 @ ">" @ %d1 @ " swing " @ OptNum(%t0) @ ">" @ OptNum(%t1) @ "s crit " @ %b0 @ ">" @ (%b0 + %ic - %cc) @ ($OptGDFx !$= "" ? " effects " @ OptFxText($OptGDFx) : "") @ " damage taken " @ OptNum($OptGearBase) @ ">" @ OptNum(%new);
   return %new - $OptGearBase;
}

// damage taken at the gear site if slot %slot held an item with these stats instead of %cur
// (all zero and %remove 1: the slot left empty). keeps the best change overall and per slot
function OptGearEval(%k, %c, %slot, %cur, %name, %src, %ia, %idf, %ih, %is, %ic, %remove)
{
   %cdb = %c.getDataBlock();
   %d = OptGearDelta(%k, %c, %slot, %cur, %ia, %idf, %ih, %is, %ic);
   %new = $OptGDNew;
   if ($OptGSlotD[%k, %slot] $= "" || %d < $OptGSlotD[%k, %slot])
   {
      $OptGSlotD[%k, %slot] = %d;
      $OptGSlotItem[%k, %slot] = %name;
      $OptGSlotRemove[%k, %slot] = %remove;
   }
   if (%d < $OptGearBest[%k])
   {
      $OptGearBest[%k] = %d;
      $OptGearNew[%k] = %new;
      $OptGearItem[%k] = %name;
      $OptGearSrc[%k] = %src;
      $OptGearRemove[%k] = %remove;
      $OptGearSlot[%k] = strreplace(%cdb.InventorySlotType[%slot], "_", " ");
      $OptGearCur[%k] = isObject(%cur) ? %cur.getNameString() : "";
      $OptGearWhy[%k] = $OptGDWhy;
      $OptGearFx[%k] = $OptGDFx;
      // worn by a townsperson: who, and in which of their slots (OptGearStep sets these)
      $OptGearWho[%k] = $OptGTryWho;
      $OptGearWhoSlot[%k] = $OptGTrySlot;
   }
}

// the slot's name as the game shows it (Weapon, Heavy Armor, Off Hand, Artifact)
function OptSlotName(%c, %slot)
{
   return strreplace(%c.getDataBlock().InventorySlotType[%slot], "_", " ");
}

// best swap per party member against site %e, from the player inventory and the town inventory.
// results in $OptGearItem/Src/Slot/Cur/Best/New[k]; $OptGearBase is the damage taken as geared now
function OptScanGear(%e)
{
   %sync = $Opt::PipeSync;
   $Opt::PipeSync = 1;
   OptGearBegin(%e);
   OptGearStep(getRealTime());
   $Opt::PipeSync = %sync;
   $OptGearSite = %e;
   $OptGearReady = 1;
}

// sets up a gear scan against site %e: the party, the site's enemies and both inventories
function OptGearBegin(%e)
{
   $OptGearGen++;
   OptLoadParty();
   // the fight you will actually take at that site: the enemy you hunt there with everyone who
   // joins it (the target search's group), else the whole site
   %f = $OptGearFocus;
   if (%f !$= "" && isObject(%f) && %f.getState() !$= "Dead" && $OptGoSite == %e.getId())
   {
      OptLoadJoiners(%f);
      $OptGearVs = OptCName(%f) @ ($OptN["E"] > 1 ? " and the " @ ($OptN["E"] - 1) @ " who join" : "") @ " at " @ OptSiteName(%e);
   }
   else
   {
      OptLoadSite(%e);
      $OptGearVs = OptSiteName(%e);
   }
   $OptGearBase = getWord(OptFight(), 0);
   if ($OptGearBase $= "inf") { $OptGearBase = 9999; }
   $OptGInvN = 0;
   %partyInv = MainCharacter.getParty().GetInventory();
   if (isObject(%partyInv)) { $OptGInv[$OptGInvN] = %partyInv; $OptGSrc[$OptGInvN] = " (player inventory)"; $OptGInvN++; }
   // what is in town (its inventory, what townsfolk wear) only counts while you are in town: the
   // guide never sends you back to town for an item (it did, right after a portal, and in town
   // the target and the pick were different again)
   %inTown = OptLordInTown();
   %townInv = gTown.GetInventory();
   if (%inTown && isObject(%townInv)) { $OptGInv[$OptGInvN] = %townInv; $OptGSrc[$OptGInvN] = " (town inventory)"; $OptGInvN++; }
   $OptGV = 0;
   $OptGS = 0;
   $OptGTryWho = "";
   $OptGTrySlot = "";
   // what your townsfolk wear, to be taken off them when it is better on you. not a work item
   // that speeds up their work, and not someone who walks in your party
   $OptGTPN = 0;
   $OptGTPI = 0;
   for (%i = 0; %inTown && %i < TownsPeople.getCount(); %i++)
   {
      %p = TownsPeople.getObject(%i);
      if (!isObject(%p) || %p.getState() $= "Dead" || MainCharacter.getParty().isMember(%p)) { continue; }
      %prof = %p.myCandidateData.Profession;
      for (%s = 0; %s < 4; %s++)
      {
         %it = OptEquipped(%p, %s);
         if (!isObject(%it) || OptWorkBonus(%it.getDataBlock(), %prof) > 0) { continue; }
         $OptGTPItem[$OptGTPN] = %it;
         $OptGTPWho[$OptGTPN] = %p;
         $OptGTPSlot[$OptGTPN] = %s;
         $OptGTPN++;
      }
   }
   for (%k = 0; %k < $OptN["P"]; %k++)
   {
      $OptGearBest[%k] = -0.05;
      $OptGearItem[%k] = "";
      $OptGearWho[%k] = "";
      $OptGearRemove[%k] = 0;
      %c = $OptObj["P", %k];
      for (%s = 0; %s < 4; %s++)
      {
         $OptGSlotD[%k, %s] = "";
         $OptGSlotItem[%k, %s] = "";
         $OptGSlotRemove[%k, %s] = 0;
      }
      // taking off what is worn is a choice too (a shield or heavy armor slows every swing)
      for (%s = 1; %s < 4; %s++)
      {
         %cur = OptEquipped(%c, %s);
         if (isObject(%cur)) { OptGDSwap("", 0); OptGearEval(%k, %c, %s, %cur, %cur.getNameString(), "", 0, 0, 0, 0, 0, 1); }
      }
   }
   // once per session, write what the lord wears to console.log so the slot read can be checked
   if (!$Opt::GearLogged)
   {
      $Opt::GearLogged = 1;
      for (%s = 0; %s < 4; %s++)
      {
         %f = MainCharacter.Inventory[%s];
         echo("Opt gear slot " @ %s @ ": " @ (%f !$= "" && isObject(%f) ? %f.getNameString() : "empty"));
      }
   }
}

// tries inventory items until the piece's time is used up; 1 when every item is done
function OptGearStep(%t0)
{
   %done = 0;
   while ($OptGV < $OptGInvN)
   {
      %inv = $OptGInv[$OptGV];
      if (!isObject(%inv)) { $OptGV++; $OptGS = 0; continue; }
      %slots = %inv.GetNumSlots();
      while ($OptGS < %slots)
      {
         if (!OptSliceLeft(%t0, %done, 8)) { return 0; }
         %it = %inv.GetItem($OptGS);
         $OptGS++;
         if (!isObject(%it)) { continue; }
         for (%k = 0; %k < $OptN["P"]; %k++) { OptTryItem(%k, $OptObj["P", %k], %it, $OptGSrc[$OptGV]); }
         %done++;
      }
      $OptGV++;
      $OptGS = 0;
   }
   // then what your townsfolk wear (listed by OptGearBegin)
   while ($OptGTPI < $OptGTPN)
   {
      if (!OptSliceLeft(%t0, %done, 8)) { return 0; }
      %n = $OptGTPI;
      $OptGTPI++;
      %it = $OptGTPItem[%n];
      %p = $OptGTPWho[%n];
      if (!isObject(%it) || !isObject(%p)) { continue; }
      $OptGTryWho = %p;
      $OptGTrySlot = $OptGTPSlot[%n];
      for (%k = 0; %k < $OptN["P"]; %k++) { OptTryItem(%k, $OptObj["P", %k], %it, " (worn by " @ %p.getNameString() @ ")"); }
      $OptGTryWho = "";
      $OptGTrySlot = "";
      %done++;
   }
   return 1;
}

function OptGearText(%e)
{
   if (!isObject(%e)) { return ""; }
   if (!$OptGearReady || $OptGearSite != %e) { OptScanGear(%e); }
   %out = "";
   for (%k = 0; %k < $OptN["P"]; %k++)
   {
      // your lord's gear is decided over every enemy left (OptWholePick), never by the next site
      if (%k == $OptLord) { continue; }
      if ($OptGearItem[%k] !$= "" && $OptGearRemove[%k])
      {
         %out = %out @ "  " @ $OptNm["P", %k] @ ": unequip " @ $OptGearItem[%k] @ " (" @ $OptGearSlot[%k] @ ", damage taken " @ OptNum($OptGearBase) @ " to " @ OptNum($OptGearNew[%k]) @ ")\n";
      }
      else if ($OptGearItem[%k] !$= "")
      {
         %out = %out @ "  " @ $OptNm["P", %k] @ ": equip " @ $OptGearItem[%k] @ $OptGearSrc[%k] @ " in " @ $OptGearSlot[%k] @ ($OptGearCur[%k] !$= "" ? " instead of " @ $OptGearCur[%k] : "") @ " (damage taken " @ OptNum($OptGearBase) @ " to " @ OptNum($OptGearNew[%k]) @ ")\n";
      }
   }
   if (%out $= "") { return ""; }
   return $Opt::Gold @ "GEAR (vs your next site)" @ $Opt::White @ "\n" @ %out;
}

// healing potions in the bag. Key 1 drinks one for the lord: +33% of max HP (Creature::UseItem)
function OptPotions()
{
   %inv = MainCharacter.getParty().GetInventory();
   if (!isObject(%inv)) { return 0; }
   %n = 0;
   %slots = %inv.GetNumSlots();
   for (%s = 0; %s < %slots; %s++)
   {
      %it = %inv.GetItem(%s);
      if (isObject(%it) && %it.getDataBlock().getId() == dbItem_Healing_Potion.getId()) { %n += %inv.GetItemAmount(%s); }
   }
   return %n;
}

// ------------------------------------------------------------------ loot
// containers the game makes: the world chests (PlaceRandomLoot, 25 on a Long map), the treasure
// chest a cleared site leaves, and the body of every killed creature (gold, potions, items).
// they are kept in OptLootSet: the spawn hooks add new ones, a slow scan finds old ones.

// a body was just made: it sits among the last objects added to GameGroup.
// it belongs to the site the creature lived at (homeEncounter), if any
function OptLootCatchNew(%creature)
{
   if (!isObject(OptLootSet) || !isObject(GameGroup)) { return; }
   %home = isObject(%creature) ? %creature.homeEncounter : "";
   %n = GameGroup.getCount();
   for (%i = %n - 1; %i >= 0 && %i >= %n - 4; %i--)
   {
      %o = GameGroup.getObject(%i);
      if (%o.getClassName() $= "HavenContainer" && !OptLootSet.isMember(%o))
      {
         OptLootSet.add(%o);
         if (isObject(%home)) { %o.optSite = %home.getId(); }
      }
   }
}

// the list of containers lives outside the mission, so leaving to the menu and loading again
// does not delete it (it was deleted that way once: every loot check then failed)
function OptLootSetMake()
{
   if (isObject(OptLootSet)) { return; }
   new SimSet(OptLootSet);
   if (isObject(RootGroup)) { RootGroup.add(OptLootSet); }
}

function OptLootScanStart()
{
   OptLootSetMake();
   $Opt::LootScanFor = MainCharacter.getId();
   $Opt::LootScanI = 0;
   if (isObject(LootChests))
   {
      for (%i = 0; %i < LootChests.getCount(); %i++)
      {
         %o = LootChests.getObject(%i);
         if (isObject(%o) && !OptLootSet.isMember(%o)) { OptLootSet.add(%o); }
      }
   }
   cancel($Opt::LootScanSchedule);
   $Opt::LootScanSchedule = schedule($Opt::SliceGap, 0, OptLootScanTick);
}

// walks GameGroup a slice at a time looking for containers
function OptLootScanTick()
{
   if (!isObject(GameGroup) || !isObject(OptLootSet)) { return; }
   %t0 = getRealTime();
   %n = GameGroup.getCount();
   %done = 0;
   while ($Opt::LootScanI < %n && %done < 150 && getRealTime() - %t0 < $Opt::SliceBudget)
   {
      %o = GameGroup.getObject($Opt::LootScanI);
      $Opt::LootScanI++;
      %done++;
      if (%o.getClassName() $= "HavenContainer" && !OptLootSet.isMember(%o)) { OptLootSet.add(%o); }
   }
   OptPerfNote(getRealTime() - %t0, 0, "lootscan");
   if ($Opt::LootScanI < %n) { $Opt::LootScanSchedule = schedule($Opt::SliceGap, 0, OptLootScanTick); }
}

function OptLootBegin()
{
   $OptLootI = 0;
   $OptLootN = 0;
   $OptLootSells = OptTownSells();
}

// reads a few containers per piece; bodies more than 400 m away are left for later
function OptLootStep(%t0)
{
   %done = 0;
   while ($OptLootI < OptLootSet.getCount())
   {
      if (!OptSliceLeft(%t0, %done, 6)) { return 0; }
      %c = OptLootSet.getObject($OptLootI);
      $OptLootI++;
      if (!isObject(%c)) { continue; }
      if (%c.IsEmpty())
      {
         OptLootSet.remove(%c);
         $OptLootI--;
         continue;
      }
      %p = %c.getPosition();
      %d = OptDist2D($OptScanLP, %p);
      // every container, however far: bodies and chests never expire, so none may be forgotten
      %done++;
      OptLootRead(%c, %p, %d);
   }
   return 1;
}

// what one container holds, and whether it is worth the walk
function OptLootRead(%c, %p, %d)
{
   %inv = %c.inventoryName;
   if (!isObject(%inv)) { return; }
   %slots = %inv.GetNumSlots();
   if (%slots $= "" || %slots < 1) { %slots = $gMaxContainerItems; }
   %gold = 0; %food = 0; %pots = 0; %other = 0; %otherName = ""; %gear = ""; %gearD = 0; %gearSlot = "";
   %L = $OptLord;
   for (%s = 0; %s < %slots; %s++)
   {
      %it = %inv.GetItem(%s);
      if (!isObject(%it)) { continue; }
      %amt = %inv.GetItemAmount(%s);
      if (%amt < 1) { %amt = 1; }
      %dn = %it.getDataBlock().getName();
      if (%dn $= "dbItem_Coin") { %gold += %amt; continue; }
      if (%dn $= "dbItem_Rations") { %food += %amt; continue; }
      if (%dn $= "dbItem_Healing_Potion") { %pots += %amt; continue; }
      // a townsperson's remains are for the undertaker, not loot
      if (%dn $= "dbItem_Remains") { continue; }
      // gear in containers within 400 m is scored as an upgrade (a fight each); farther ones are
      // only listed by name, so hundreds of far bodies never cost hundreds of fights
      %gd = ($OptGearReady && %d <= 400) ? OptLootGearDelta(%it) : "";
      if (%gd !$= "" && %gd < %gearD) { %gearD = %gd; %gear = %it.getNameString(); %gearSlot = $OptLootGearSlot; }
      // an upgrade is listed as one; other gear counts like any item (it sells at a Market)
      if (%gd !$= "" && %gd <= -0.5) { continue; }
      %other++;
      if (%otherName $= "") { %otherName = %it.getNameString(); }
   }
   // items left behind in a container you already opened are not counted again, until a Market
   // with its Merchant can buy them
   %fresh = !%c.openedBefore || $OptLootSells;
   %upgrade = (%gear !$= "" && %gearD <= -0.5);
   if (!%gold && !%food && !%pots && !%upgrade && !(%fresh && %other > 0)) { return; }
   // the site it belongs to: the one it was tagged with when it appeared, else the site whose
   // 64 m square it lies in (none for most world chests). OptLootPick only sends you to it when
   // no enemy stands near it, cleared site or not
   %enc = %c.optSite;
   if (!isObject(%enc) && isFunction("GetEncounterFromMasterGrid")) { %enc = GetEncounterFromMasterGrid(%p); }
   %guard = "";
   %cleared = 1;
   if (isObject(%enc) && !%enc.won)
   {
      %cleared = 0;
      %guard = %enc.getId();
   }
   // LEFT BEHIND: a body (you made it), anything inside the square of a site you have won, and a
   // chest on open ground within 20 m of a won site's square (right at its edge). these come
   // before the next site, however far (OptLootPick). every other chest (open ground farther
   // out, or inside a site not won yet) is not left behind: only when it is on your way
   %lost = %c.spawnedFromCreature ? 1 : 0;
   %lostAt = "";
   if (isObject(%enc) && %enc.won) { %lost = 1; %lostAt = "at " @ OptSiteName(%enc) @ " (cleared)"; }
   else if (!isObject(%enc))
   {
      %near = OptWonSiteNear(%p, 20);
      if (isObject(%near)) { %lost = 1; %lostAt = "next to " @ OptSiteName(%near) @ " (cleared)"; }
   }
   %n = $OptLootN;
   $OptLootObj[%n] = %c;
   $OptLootPos[%n] = %p;
   $OptLootDist[%n] = %d;
   $OptLootGuard[%n] = %guard;
   $OptLootCleared[%n] = %cleared;
   $OptLootLost[%n] = %lost;
   $OptLootLostAt[%n] = %lostAt;
   $OptLootKind[%n] = %c.spawnedFromCreature ? "body" : "chest";
   $OptLootSig[%n] = OptLootSig(%c);
   %w = "";
   if (%upgrade) { %w = %w @ ", " @ %gear @ " (better " @ %gearSlot @ ", " @ OptNum(%gearD) @ " damage taken)"; }
   if (%pots) { %w = %w @ ", " @ %pots @ " potion" @ (%pots == 1 ? "" : "s"); }
   if (%food) { %w = %w @ ", " @ %food @ " food"; }
   if (%gold) { %w = %w @ ", " @ %gold @ " gold"; }
   if (%fresh && %other > 0) { %w = %w @ ", " @ (%other == 1 ? %otherName : %otherName @ " +" @ (%other - 1) @ " more"); }
   $OptLootWhat[%n] = getSubStr(%w, 2, 999);
   $OptLootN++;
}

// gear change (damage taken at the gear site) if the lord wore this item, or "" when it is not gear
function OptLootGearDelta(%it)
{
   %idb = %it.getDataBlock();
   if (%idb.Useable) { return ""; }
   if (%idb.category $= "Weapon" && %idb.ActionType !$= "melee attack" && %idb.ActionType !$= "missile attack") { return ""; }
   %L = $OptLord;
   %c = $OptObj["P", %L];
   if (!isObject(%c)) { return ""; }
   %slot = OptItemSlot(%c, %idb);
   if (%slot $= "" || OptHandsBlocked(%c, %idb, %slot)) { return ""; }
   $OptLootGearSlot = strreplace(%c.getDataBlock().InventorySlotType[%slot], "_", " ");
   %cur = OptEquipped(%c, %slot);
   %spd = %it.getSpeed() + OptWeaponBonus(%c.getDataBlock(), %idb);
   OptGDSwap(%it, 0);
   return OptGearDelta(%L, %c, %slot, %cur, %it.getAttack(), %it.getDefense(), %it.getHealth(), %spd, %it.getCriticalBonus());
}

// a won site whose 64 m square (encounterPosition is its corner) lies within %r of %pos, measured
// to the square's edge, or 0
function OptWonSiteNear(%pos, %r)
{
   %x = getWord(%pos, 0);
   %y = getWord(%pos, 1);
   %cnt = Encounters.getCount();
   for (%j = 0; %j < %cnt; %j++)
   {
      %e = Encounters.getObject(%j);
      if (!isObject(%e) || !%e.won) { continue; }
      %ep = %e.encounterPosition;
      %x0 = getWord(%ep, 0);
      %y0 = getWord(%ep, 1);
      %dx = OptMax(OptMax(%x0 - %x, %x - (%x0 + 64)), 0);
      %dy = OptMax(OptMax(%y0 - %y, %y - (%y0 + 64)), 0);
      if (%dx * %dx + %dy * %dy <= %r * %r) { return %e; }
   }
   return 0;
}

// A SITE NOT SEEN UP CLOSE IS NOT EMPTY GROUND. its enemies do not exist until you come close
// (the game makes them then), so every test that looks for enemies finds nobody there: the guide
// sent a level 4 lord to a chest inside a 7-square Fortress he had not seen, "no enemy within
// 35 m" was true, three orcs stood 5 m from it when he arrived. the site not won and not seen
// whose 64 m square lies within %r of %pos (measured to the square's edge), or 0. sites that
// spawn nothing (no MonsterSet) don't count
function OptUnseenSiteNear(%pos, %r)
{
   if (!isObject(Encounters)) { return 0; }
   %x = getWord(%pos, 0);
   %y = getWord(%pos, 1);
   %cnt = Encounters.getCount();
   for (%j = 0; %j < %cnt; %j++)
   {
      %e = Encounters.getObject(%j);
      if (!isObject(%e) || %e.won || !isObject(%e.MonsterSet)) { continue; }
      if (%e.spawnPlaced && isObject(%e.memberSet)) { continue; }
      %ep = %e.encounterPosition;
      %x0 = getWord(%ep, 0);
      %y0 = getWord(%ep, 1);
      %dx = OptMax(OptMax(%x0 - %x, %x - (%x0 + 64)), 0);
      %dy = OptMax(OptMax(%y0 - %y, %y - (%y0 + 64)), 0);
      if (%dx * %dx + %dy * %dy <= %r * %r) { return %e; }
   }
   return 0;
}

// the nearest container worth opening that no enemy stands near: its index, or -1.
// %lostOnly 1: only loot left behind (bodies, anything at or near a site you won), anywhere on
// the map: it comes before the next site. %lostOnly 0: any container, and with %go (where the
// target search sends you) only one on the way there, at most 60 m more walking than going
// straight: other chests wait until they are on your way
function OptLootPick(%go, %lostOnly)
{
   if (!$OptLootReady) { return -1; }
   %lp = MainCharacter.getPosition();
   %direct = (%go !$= "") ? OptDist2D(%lp, %go) : 0;
   for (%i = 0; %i < $OptLootN; %i++) { $OptLootSkip[%i] = 0; }
   %best = -1;
   // the 10 nearest are checked, nearest first
   for (%tries = 0; %tries < 10; %tries++)
   {
      %best = -1;
      for (%i = 0; %i < $OptLootN; %i++)
      {
         if ($OptLootSkip[%i]) { continue; }
         if (!isObject($OptLootObj[%i])) { continue; }
         if (%lostOnly && !$OptLootLost[%i]) { continue; }
         if (!%lostOnly && %go !$= "" && OptDist2D(%lp, $OptLootPos[%i]) + OptDist2D($OptLootPos[%i], %go) - %direct > 60) { continue; }
         if (%best < 0 || $OptLootDist[%i] < $OptLootDist[%best]) { %best = %i; }
      }
      if (%best < 0) { break; }
      // emptied or taken from since the list was read (the list is only read in a full pass):
      // off the list at once, for every loot line, until the next full pass reads it again
      if (!OptLootStill(%best))
      {
         $OptLootObj[%best] = 0;
         %best = -1;
         continue;
      }
      // an enemy within 20 m of it would join you there (18 m on Hard): fight first, loot after.
      // a chest is only first given with nobody within 35 m: enemies wander, and one stepping
      // next to a chest you are walking to took the step away half way (once given it is only
      // put on hold at 20 m, OptLootKept). bodies lie where you just fought: 20 m
      // and never one in or within 35 m of a site you have not seen: its enemies are made when
      // you get there (OptUnseenSiteNear)
      if (!isObject(OptUnseenSiteNear($OptLootPos[%best], 35)) && !OptEnemyNear($OptLootPos[%best], $OptLootKind[%best] $= "chest" ? 35 : 20)) { break; }
      $OptLootSkip[%best] = 1;
      %best = -1;
   }
   for (%i = 0; %i < $OptLootN; %i++) { $OptLootSkip[%i] = 0; }
   return %best;
}

// what a container holds right now, as one word per item (id x amount)
function OptLootSig(%c)
{
   %inv = %c.inventoryName;
   if (!isObject(%inv)) { return ""; }
   %slots = %inv.GetNumSlots();
   if (%slots $= "" || %slots < 1) { %slots = $gMaxContainerItems; }
   %sig = "";
   for (%s = 0; %s < %slots; %s++)
   {
      %it = %inv.GetItem(%s);
      if (isObject(%it)) { %sig = %sig @ %it.getId() @ "x" @ %inv.GetItemAmount(%s) @ " "; }
   }
   return %sig;
}

// 1 while listed container %i is still there and holds exactly what it was listed with
function OptLootStill(%i)
{
   %c = $OptLootObj[%i];
   if (!isObject(%c) || %c.IsEmpty()) { return 0; }
   return OptLootSig(%c) $= $OptLootSig[%i];
}

// the container the last loot step sent you to ($OptLootKeepObj), as its place in the list now:
// it stays your loot step until it is opened or gone. -1 when there is none, or while an enemy
// stands within 20 m of it (the step waits, the container is kept). forgotten in town: each
// trip from town starts afresh, like the target site
function OptLootKept()
{
   %o = $OptLootKeepObj;
   if (%o $= "") { return -1; }
   if (!isObject(%o) || OptLordInTown()) { $OptLootKeepObj = ""; return -1; }
   if (!$OptLootReady) { return -1; }
   %id = %o.getId();
   for (%i = 0; %i < $OptLootN; %i++)
   {
      %c = $OptLootObj[%i];
      if (!isObject(%c) || %c.getId() != %id) { continue; }
      if (!OptLootStill(%i)) { $OptLootObj[%i] = 0; break; }
      // in or next to a site you have not seen: never kept (see OptUnseenSiteNear)
      if (isObject(OptUnseenSiteNear($OptLootPos[%i], 35))) { $OptLootKeepObj = ""; return -1; }
      return OptEnemyNear($OptLootPos[%i], 20) ? -1 : %i;
   }
   // not on the list any more: opened, emptied or taken from
   if (%i >= $OptLootN || !isObject($OptLootObj[%i])) { $OptLootKeepObj = ""; }
   return -1;
}

// 1 when a living enemy stands within %r of %pos
function OptEnemyNear(%pos, %r)
{
   if (!isObject(enemyFactionSet)) { return 0; }
   %x = getWord(%pos, 0);
   %y = getWord(%pos, 1);
   %cnt = enemyFactionSet.getCount();
   for (%j = 0; %j < %cnt; %j++)
   {
      %c = enemyFactionSet.getObject(%j);
      if (!isObject(%c) || %c.getState() $= "Dead") { continue; }
      %p = %c.getPosition();
      %dx = getWord(%p, 0) - %x;
      %dy = getWord(%p, 1) - %y;
      if (%dx * %dx + %dy * %dy <= %r * %r) { return 1; }
   }
   return 0;
}

// what a guarded chest inside site %e holds, or ""
function OptLootAtSite(%e)
{
   if (!$OptLootReady || !isObject(%e)) { return ""; }
   for (%i = 0; %i < $OptLootN; %i++)
   {
      if ($OptLootGuard[%i] !$= "" && $OptLootGuard[%i] == %e.getId()) { return $OptLootWhat[%i]; }
   }
   return "";
}

// details view: the five nearest containers worth opening
function OptLootText()
{
   if (!$OptLootReady || $OptLootN < 1) { return ""; }
   for (%i = 0; %i < $OptLootN; %i++) { %used[%i] = 0; }
   %t = $Opt::Gold @ "LOOT" @ $Opt::White @ "\n";
   for (%k = 0; %k < 5 && %k < $OptLootN; %k++)
   {
      %b = -1;
      for (%i = 0; %i < $OptLootN; %i++)
      {
         if (%used[%i]) { continue; }
         if (%b < 0 || $OptLootDist[%i] < $OptLootDist[%b]) { %b = %i; }
      }
      %used[%b] = 1;
      %g = $OptLootGuard[%b] !$= "" ? $Opt::Orange @ " · guarded by " @ OptSiteName($OptLootGuard[%b]) @ $Opt::White : "";
      %t = %t @ "  " @ ($OptLootKind[%b] $= "body" ? "Body" : "Chest") @ " (" @ OptWhere($OptLootPos[%b]) @ "): " @ $OptLootWhat[%b] @ %g @ "\n";
   }
   return %t;
}

// free slots in the player inventory
function OptFreeSlots()
{
   %inv = MainCharacter.getParty().GetInventory();
   if (!isObject(%inv)) { return 0; }
   %n = 0;
   %slots = %inv.GetNumSlots();
   for (%s = 0; %s < %slots; %s++)
   {
      if (!isObject(%inv.GetItem(%s))) { %n++; }
   }
   return %n;
}

// ------------------------------------------------------------------ hiring advice

// a townsperson whose building makes food lives in town
function OptHasFoodWorker()
{
   for (%i = 0; %i < TownsPeople.getCount(); %i++)
   {
      %p = TownsPeople.getObject(%i);
      if (!isObject(%p) || %p.getState() $= "Dead") { continue; }
      %b = %p.getHomeBuilding();
      if (isObject(%b) && %b.getDataBlock().Food_Produced > 0) { return 1; }
   }
   return 0;
}

// a visitor the guide hires for food: FARMERS ONLY (the player's rule, back again: "let's remove
// herders and trappers. farmers only"; any food worker had been tried to fill the town sooner).
// a Farmer's building grows (Grain Farm 1.5 > Large Farm 3 > Plantation 5 a day); a Herder's
// stays 1.5 and a Trapper's reaches 3. Herders and Trappers already in town count as food workers
// until a visiting Farmer takes their place (OptSwapPick). to find Farmers sooner, the Goodman's
// visitor slots come first while Farmers are short (OptIsSlotsHire)
function OptMakesFood(%c)
{
   %cd = %c.myCandidateData;
   if (!isObject(%cd)) { return 0; }
   if (%cd.Profession !$= "Farmer" && %cd.Profession !$= "OrcFarmer") { return 0; }
   return OptFoodRank(%cd.Profession) > 0;
}

// THE GOODMAN FOR MORE VISITORS (the player: "in early game only 2 townsfolk appear each time, so
// it's very hard to find farmers"). the visitors at a time are the Candidate_Bonus of the occupied
// buildings (PlayerTown::getNumHireableCandidates): your house 2 at every level, the Goodman's
// Hostel +1, Inn +2, Tavern +3. while fewer than 6 Farmers live in town his line comes first:
// hired before the first Farmer too, as long as the food in store feeds him (OptGoodmanFoodOk),
// and its upgrades are bought for the visitors even when their gold pays back slowly.
// 1 for a Goodman visitor to hire that way
function OptIsSlotsHire(%c)
{
   if (!OptFarmersShort() || !isObject(%c.myCandidateData)) { return 0; }
   %gl = OptLineIndex("gold");
   if (%gl < 0 || %c.myCandidateData.Profession !$= getWord($OptLine[%gl], 1)) { return 0; }
   OptTownScan();
   if (isObject($OptLW[%gl])) { return 0; }
   return OptGoodmanFoodOk();
}

// 1 when the food in store lasts 10 shown days or more with one more mouth
function OptGoodmanFoodOk()
{
   %net = OptFoodNet() - 1;
   if (%net >= 0) { return 1; }
   return gTown.Food / (-%net) >= 10;
}

// the most food a building reaches along its upgrades (Food_Produced a game day), 0 for none
function OptFoodChain(%bdb)
{
   %best = 0;
   for (%n = 0; %n < 6 && isObject(%bdb); %n++)
   {
      if (%bdb.Food_Produced > %best) { %best = %bdb.Food_Produced; }
      %bdb = %bdb.Upgrades_To_1;
   }
   return %best;
}

// the same for a profession's own building: a visitor's kind as a food worker
function OptFoodRank(%prof)
{
   if (%prof $= "") { return 0; }
   return OptFoodChain(getBaseBuildingByProfession(%prof));
}

// a townsperson's kind as a food worker: the most food his home reaches, 0 for none
function OptWorkerFoodRank(%p)
{
   %b = %p.getHomeBuilding();
   if (!isObject(%b)) { return 0; }
   return OptFoodChain(%b.getDataBlock());
}

// food workers living in town, any kind
function OptFoodWorkers()
{
   %n = 0;
   for (%i = 0; %i < TownsPeople.getCount(); %i++)
   {
      %p = TownsPeople.getObject(%i);
      if (!isObject(%p) || %p.getState() $= "Dead") { continue; }
      if (OptWorkerFoodRank(%p) > 0) { %n++; }
   }
   return %n;
}

// Farmers living in town
function OptFarmerCount()
{
   %n = 0;
   for (%i = 0; %i < TownsPeople.getCount(); %i++)
   {
      %p = TownsPeople.getObject(%i);
      if (!isObject(%p) || %p.getState() $= "Dead" || !isObject(%p.myCandidateData)) { continue; }
      if (%p.myCandidateData.Profession $= "Farmer" || %p.myCandidateData.Profession $= "OrcFarmer") { %n++; }
   }
   return %n;
}

// FOOD WORKERS UP TO 6 (the player's rule: "we need 6 anyway ... we fill the 6 farmers first,
// then care about levels of townsfolk later", then any food worker counts, see OptMakesFood).
// while fewer than $Opt::Farmers food workers live in town, every food worker who visits is
// hired, whatever the food. 1 while short
function OptFoodShort()
{
   return OptFoodWorkers() < $Opt::Farmers;
}

// 1 while fewer than 6 Farmers live in town (the plan's 6 Plantations): the other kinds are still
// being traded up (OptSwapPick)
function OptFarmersShort()
{
   return OptFarmerCount() < $Opt::Farmers;
}

// 1 when this visitor is a food worker hired as one more: food workers short (OptFoodShort), and
// a building lot free when no empty building of his kind is there
function OptFarmerWanted(%c)
{
   if (!OptMakesFood(%c) || !OptFoodShort()) { return 0; }
   return !(OptNeedsLot(%c.myCandidateData.Profession) && OptFreeLots() < 1);
}

// returns "useful|cost|text|netfood" for a visitor, from its building's data.
// net food = what they make per shown day minus what they eat (buildings pay out every
// game day, everyone eats once per shown day, as in PlayerTown::GetRemainingDaysFood)
function OptHireInfo(%c)
{
   %cyc = $gDayApparentCycle;
   %cd = %c.myCandidateData;
   if (!isObject(%cd)) { return "0|0||0"; }
   %b = getBaseBuildingByProfession(%cd.Profession);
   %cost = %c.HireCost;
   if (isObject(%b))
   {
      %empty = getEmptyFollowerBuildings(%cd.Profession);
      %ne = %empty.getCount();
      %empty.delete();
      if (%ne <= 0) { %cost += mFloor(%b.Cost_Gold * gTown.BuildCostMultiplier); }
   }
   %eats = %c.foodConsumption;
   if (%eats $= "") { %eats = 1; }
   %what = "";
   %useful = 0;
   %netFood = -%eats;
   if (isObject(%b))
   {
      %food = 0; %gold = 0; %fame = 0;
      if (%b.Food_Produced) { %food = (%b.Food_Produced + %c.productionBonus + gTown.foodProductionBonus) * %cyc; }
      if (%b.Gold_Produced) { %gold = (%b.Gold_Produced + %c.productionBonus + gTown.goldProductionBonus) * %cyc; }
      if (%b.Fame_Produced) { %fame = (%b.Fame_Produced + %c.productionBonus + gTown.fameProductionBonus) * %cyc; }
      %netFood = %food - %eats;
      %what = %what @ ", food " @ (%netFood >= 0 ? "+" : "") @ OptNum(%netFood) @ "/day after eating";
      if (%gold > 0) { %what = %what @ ", +" @ OptNum(%gold) @ " gold/day"; }
      if (%fame > 0) { %what = %what @ ", +" @ OptNum(%fame) @ " fame/day"; }
      if (%b.Production_Set !$= "") { %what = %what @ ", makes items"; }
      if (%b.Attack > 0) { %what = %what @ ", tower shoots raiders"; }
      if (%netFood > 0) { %useful = 1; }
   }
   %t = %c.getProfessionName() @ " L" @ OptShown(%c.Level) @ ", " @ %cost @ " gold";
   if (gTown.Fame < %c.FameRequired) { %t = %t @ ", needs " @ %c.FameRequired @ " fame"; }
   if (%cost > gTown.Gold) { %t = %t @ ", can't afford"; }
   return %useful @ "|" @ %cost @ "|" @ %t @ %what @ "|" @ %netFood;
}

// ------------------------------------------------------------------ farm route
// a Grain Farm makes 0.3 food a game day; a Knight's town takes 0.1 off, and a farmer eats 1
// per shown day (5 game days), so (0.3 - 0.1) * 5 - 1 = 0: he only feeds himself. upgraded to a
// Large Farm (0.6, needs a Manor) he gives (0.6 - 0.1) * 5 - 1 = +1.5 food a day.

function OptHasManor()
{
   return gTown.HasBuilding(dbBuildingManor);
}

// gold to turn the Outpost into a Manor (0 when there is one)
function OptManorCost()
{
   if (OptHasManor() || !isObject($manorHouse)) { return 0; }
   return dbBuildingManor.getUpgradeCost($manorHouse.getDataBlock());
}

// food a shown day this building makes with this worker's bonus, after the town's class bonus
function OptFarmFood(%bdb, %bonus)
{
   return (%bdb.Food_Produced + %bonus + gTown.foodProductionBonus) * $gDayApparentCycle;
}

// the farm upgrade a worker's building can take for more food, if it can be built: a Manor can
// be bought on the way; every other required building, the town quality and every resource
// access (the Plantation's Fresh Water, the Hunting Lodge's Game) must already be there, as the
// game checks them all (OptReqMissing). its datablock or ""
function OptFoodUpgrade(%bdb)
{
   %up = %bdb.Upgrades_To_1;
   if (!isObject(%up) || %up.Food_Produced <= %bdb.Food_Produced) { return ""; }
   %r1 = %up.Required_Building_1;
   %r2 = %up.Required_Building_2;
   if (%r1 !$= "" && %r1 !$= "none" && !gTown.HasBuilding(%r1) && %r1 !$= "dbBuildingManor") { return ""; }
   if (%r2 !$= "" && %r2 !$= "none" && !gTown.HasBuilding(%r2)) { return ""; }
   if (%up.Town_Quality_Required > 0 && gTown.getTownQuality() < %up.Town_Quality_Required) { return ""; }
   for (%k = 1; %k <= 2; %k++)
   {
      %a = (%k == 1) ? %up.Required_Access_1 : %up.Required_Access_2;
      if (%a !$= "" && isObject(%a) && !gTown.getResourceAccess(%a.Name)) { return ""; }
      %ci = (%k == 1) ? %up.Cost_Item_1 : %up.Cost_Item_2;
      if (%ci !$= "" && %ci !$= "none" && !gTown.HasItemFromSet(%ci)) { return ""; }
   }
   return %up;
}

// a townsperson whose farm can still be upgraded for food: the first one, or 0
function OptFarmToUpgrade()
{
   for (%i = 0; %i < TownsPeople.getCount(); %i++)
   {
      %p = TownsPeople.getObject(%i);
      if (!isObject(%p) || %p.getState() $= "Dead") { continue; }
      %b = %p.getHomeBuilding();
      if (!isObject(%b)) { continue; }
      if (OptFoodUpgrade(%b.getDataBlock()) !$= "") { return %p; }
   }
   return 0;
}

// food a day the town will have once every farm that can be upgraded is upgraded
function OptFoodNetPlanned()
{
   %net = OptFoodNet();
   for (%i = 0; %i < TownsPeople.getCount(); %i++)
   {
      %p = TownsPeople.getObject(%i);
      if (!isObject(%p) || %p.getState() $= "Dead") { continue; }
      %b = %p.getHomeBuilding();
      if (!isObject(%b)) { continue; }
      %bdb = %b.getDataBlock();
      %up = OptFoodUpgrade(%bdb);
      if (%up !$= "") { %net += OptFarmFood(%up, %p.productionBonus) - OptFarmFood(%bdb, %p.productionBonus); }
   }
   return %net;
}

// what a visitor's food would be once his farm is upgraded: "net|extra gold|upgrade name",
// or "" when his building has no food upgrade within reach
function OptHireFarmPlan(%c)
{
   %cd = %c.myCandidateData;
   if (!isObject(%cd)) { return ""; }
   %b = getBaseBuildingByProfession(%cd.Profession);
   if (!isObject(%b)) { return ""; }
   %up = OptFoodUpgrade(%b);
   if (%up $= "") { return ""; }
   %eats = %c.foodConsumption;
   if (%eats $= "") { %eats = 1; }
   %net = OptFarmFood(%up, %c.productionBonus) - %eats;
   if (%net <= 0) { return ""; }
   %gold = %up.getUpgradeCost(%b);
   if (%up.Required_Building_1 $= "dbBuildingManor") { %gold += OptManorCost(); }
   return %net @ "|" @ %gold @ "|" @ getStringTableEntry(%up.StringIDName);
}

// the visitor to hire for food: a farm route if one is affordable, else the best plain food
// producer. 0 when nobody fits
function OptBestFarmHire()
{
   %best = 0;
   // while food workers are short (OptFoodShort) any food worker counts, even one who only feeds
   // himself until his farm is upgraded. the best kind first (OptFoodRank: Farmer > Trapper >
   // Herder), then the most food a day
   %floor = OptFoodShort() ? -1000 : 0;
   %bestNet = %floor;
   %bestRank = 0;
   // a Farmer with no empty farm needs a building lot (OptFreeLots)
   %lots = OptFreeLots();
   for (%i = 0; %i < $MaxNumberOfCandidates; %i++)
   {
      %c = gTown.AvailableCandidates[%i];
      if (!isObject(%c) || gTown.Fame < %c.FameRequired || !OptMakesFood(%c)) { continue; }
      if (%lots < 1 && OptNeedsLot(%c.myCandidateData.Profession)) { continue; }
      %info = strreplace(OptHireInfo(%c), "|", "\t");
      %cost = getField(%info, 1);
      %net = getField(%info, 3);
      %plan = strreplace(OptHireFarmPlan(%c), "|", "\t");
      if (%plan !$= "" && %cost + getField(%plan, 1) <= gTown.Gold && getField(%plan, 0) > %net) { %net = getField(%plan, 0); }
      else if (%cost > gTown.Gold) { continue; }
      if (%net <= %floor) { continue; }
      %rank = OptFoodRank(%c.myCandidateData.Profession);
      if (!isObject(%best) || %rank > %bestRank || (%rank == %bestRank && %net > %bestNet))
      {
         %best = %c;
         %bestNet = %net;
         %bestRank = %rank;
      }
   }
   return %best;
}

// the next click on the food route, if one can be made now: sets the step and returns 1.
// a farmer is hired while the town, farms upgraded, makes less than %margin food a day spare
function OptFoodRoute(%margin)
{
   if (%margin $= "") { %margin = 0; }
   %town = gTown.getTownCenter();
   %there = OptLordInTown();
   // FARM UPGRADES ONLY WHEN THE FOOD NEEDS THEM (the player: "why spend so much money on
   // upgrading farms to large farms and plantations ... I'm holding 381 food, producing 22.5 a day
   // while consuming 8"; the upgrades took the gold the Market route needed): a farm is upgraded
   // only while the town makes less than %margin food a day spare right now (OptFoodNet), so the
   // upgrades come as the town grows and eats more
   %p = (OptFoodNet() < %margin) ? OptFarmToUpgrade() : 0;
   if (isObject(%p))
   {
      %b = %p.getHomeBuilding();
      %bdb = %b.getDataBlock();
      %up = OptFoodUpgrade(%bdb);
      %gain = OptFarmFood(%up, %p.productionBonus) - OptFarmFood(%bdb, %p.productionBonus);
      %upName = getStringTableEntry(%up.StringIDName);
      if (%up.Required_Building_1 $= "dbBuildingManor" && !OptHasManor())
      {
         %mc = OptManorCost();
         if (gTown.Gold < %mc) { return 0; }
         OptNow("Upgrade your Outpost to a Manor", "click your portrait (top right) and press Yes · " @ %mc @ " gold · then the " @ getStringTableEntry(%bdb.StringIDName) @ " can become a " @ %upName @ " (+" @ OptNum(%gain) @ " food a day)", %there ? "" : %town);
         return 1;
      }
      %uc = %up.getUpgradeCost(%bdb);
      if (gTown.Gold < %uc) { return 0; }
      OptNow("Upgrade the " @ getStringTableEntry(%bdb.StringIDName) @ " to a " @ %upName, "click " @ %p.getNameString() @ " and press the upgrade button · " @ %uc @ " gold · +" @ OptNum(%gain) @ " food a day", %there ? "" : %b.getPosition());
      return 1;
   }
   // FOOD WORKERS UP TO 6 (OptFoodShort): a food worker who visits is hired whatever the food
   if (OptFoodNetPlanned() >= %margin && !OptFoodShort()) { return 0; }
   %c = OptBestFarmHire();
   if (!isObject(%c)) { return 0; }
   %info = strreplace(OptHireInfo(%c), "|", "\t");
   %plan = strreplace(OptHireFarmPlan(%c), "|", "\t");
   %then = (%plan !$= "") ? " · then " @ (OptHasManor() ? "" : "Manor + ") @ getField(%plan, 2) @ ": +" @ OptNum(getField(%plan, 0)) @ " food a day" : "";
   OptNow("Hire the " @ %c.getProfessionName() @ " (Visitors, top left)", getField(%info, 2) @ %then @ (OptFoodShort() ? " · food worker " @ (OptFoodWorkers() + 1) @ " of " @ $Opt::Farmers : "") @ (OptNeedGuardian() ? " · keep " @ OptHim(%c) @ " alive (Guardian)" : ""), %there ? "" : %town);
   return 1;
}

// under 3 days of food: a line for the top of the guide that flashes red and yellow (OptShow turns
// @FLASH@ into the color) and says what to do, or ""
function OptFoodWarn()
{
   %net = OptFoodNet();
   if (%net >= 0 || gTown.Food / (-%net) >= 3) { return ""; }
   %days = OptNum(gTown.Food / (-%net));
   %p = OptFarmToUpgrade();
   %c = OptBestFarmHire();
   if (isObject(%p))
   {
      %bdb = %p.getHomeBuilding().getDataBlock();
      %up = OptFoodUpgrade(%bdb);
      %gain = OptFarmFood(%up, %p.productionBonus) - OptFarmFood(%bdb, %p.productionBonus);
      %need = %up.getUpgradeCost(%bdb);
      if (%up.Required_Building_1 $= "dbBuildingManor" && !OptHasManor()) { %need += OptManorCost(); }
      %upName = getStringTableEntry(%up.StringIDName);
      if (gTown.Gold >= %need) { %do = "upgrade to the " @ %upName @ " now (+" @ OptNum(%gain) @ " food a day)"; }
      else { %do = "get " @ (%need - mFloor(gTown.Gold)) @ " more gold for the " @ %upName @ " (+" @ OptNum(%gain) @ " food a day)"; }
   }
   else if (isObject(%c)) { %do = "hire the " @ %c.getProfessionName() @ " visiting now (Visitors, top left)"; }
   else { %do = "hire the next Farmer who visits"; }
   return "@FLASH@LOW FOOD! " @ %days @ " days left: " @ %do @ " · take Gatherer if a level-up offers it (+1 a day)" @ $Opt::White @ "\n";
}

// ------------------------------------------------------------------ town plan
// Visitors only ever offer base workers: generateNewCandidates skips any profession whose
// building is an upgrade (Smithy, Inn, Armory...). Every advanced worker comes from upgrading a
// base one, and the upgrade costs the difference in building price. So the town is a set of
// lines: one base worker hired, then upgraded stage by stage.
// Lines in priority order: gold first (it pays for the rest), then fighting power.
//   gold     Goodman   Hostel 1 gold/game day > Inn (unlocks Market) > Tavern 2 gold
//   market   Merchant  Market 2 gold/game day (needs Manor + Inn)
//   weapons  Tinker    Workshop (sells 3/day) > Smithy > Weaponsmith (Bastard Sword, Fine Halberd)
//   potions  Herbalist Apothecary (healing potions), and it STAYS one (the player's rule: "stop
//            the potions line from upgrading to infirmary - unless we need infirmary for a
//            different town plan"; the Infirmary also makes Rejuvenation Potions he doesn't
//            want). > Infirmary only when another line's next building lacks nothing but town
//            quality that its Healthcare (+2) brings (OptInfirmaryFor)
//   armor    Tinker    Workshop > Smithy > Armory (Platemail, Tower Shield)
//   fortune  FortuneTeller Tent 1 gold/game day > Hut (Ring of Power)
//   magic    Herbalist Apothecary > Lab > Wizard's Tower (Amulet of Power, Boots of Protection)
//   bows     Tinker    Workshop > Fletchery (bows, for David)
//   potions2 to potions18  Herbalist  Apothecary, never upgraded (the player's rule: "add extra
//            herbalists kept as apothecaries to the town plan"). an Apothecary makes only Healing
//            Potions (PS_Simple_Potions); the Infirmary also makes Rejuvenation Potions, which he
//            doesn't want. last in the order: hired when the lines above have nothing to buy, and
//            only while the farms feed one more
//   THE ENDGAME TOWN (the player's rule, for the 28 building lots of the town; NO GUARDS, his
//   rule: "drop them". a Guard House makes nothing at all and a Watch Tower only shoots raiders
//   (attack 20), while each Guard eats 1 food a day and takes a lot; Guardian, the one thing they
//   protected, he already has). 6 Farmers at Plantations feed 29 mouths (28 townsfolk and you, 30
//   food a day; every food worker who visits is hired until 6 live in town, whatever the food,
//   OptFoodShort, and the weaker kinds are traded for better ones until all 6 are Farmers,
//   OptSwapPick). every other lot goes to the lines above, and every lot they leave to more
//   Herbalists. per lot nothing beats a Herbalist (read from the buildings' data): his Apothecary
//   makes a Healing Potion every 10 game days AND adds Town_Healing_Bonus 6 to the town's healing
//   rate (base HealingRate 10: you and the townsfolk heal 10 + 6 per Apothecary HP every 24 s in
//   town, PlayerTown::tryAddBuildingOnLot), or sells for 6 gold a day. the other buildings a
//   visitor can fill again and again give less: a Workshop sells 3 gold, a Smithy 6 (Ore), a
//   Lab 8 (offensive potions the guide never uses), a Farmer past the 6 only food nobody eats, a
//   Guard nothing. the unique ones (Shrine, Stage, Graveyard) add town quality or a little fame,
//   which the plan only needs for the Lab (3) and the Wizard's Tower (5). potions10-15 take the 6
//   lots the Guards had; potions16-18 take the lots the lines that may never be worth it leave
//   free (bows only for David, the Hut needs a crystal ball, the magic line only for its gear).
//   no hire that needs a new building is made while every lot is taken (OptFreeLots)
// Gold is ranked by payback (cost / gold a day); anything that pays back within
// $Opt::QuickPayback shown days is taken first, the rest follows the order above.
$Opt::QuickPayback = 10;
$Opt::WorthPayback = 20;
$Opt::Farmers = 6;
$Opt::FreeSlots = 1;
$OptLineN = 25;
$OptLine[0] = "gold Goodman dbBuildingHostel dbBuildingInn dbBuildingTavern";
$OptLine[1] = "market Merchant dbBuildingMarket";
$OptLine[2] = "weapons Tinker dbBuildingWorkshop dbBuildingSmithy dbBuildingWeaponsmith";
$OptLine[3] = "potions Herbalist dbBuildingApothecary dbBuildingInfirmary";
$OptLine[4] = "armor Tinker dbBuildingWorkshop dbBuildingSmithy dbBuildingArmory";
$OptLine[5] = "fortune FortuneTeller dbBuildingTent dbBuildingHut";
$OptLine[6] = "magic Herbalist dbBuildingApothecary dbBuildingLab dbBuildingTower";
$OptLine[7] = "bows Tinker dbBuildingWorkshop dbBuildingFletchery";
$OptLine[8] = "potions2 Herbalist dbBuildingApothecary";
$OptLine[9] = "potions3 Herbalist dbBuildingApothecary";
$OptLine[10] = "potions4 Herbalist dbBuildingApothecary";
$OptLine[11] = "potions5 Herbalist dbBuildingApothecary";
$OptLine[12] = "potions6 Herbalist dbBuildingApothecary";
$OptLine[13] = "potions7 Herbalist dbBuildingApothecary";
$OptLine[14] = "potions8 Herbalist dbBuildingApothecary";
$OptLine[15] = "potions9 Herbalist dbBuildingApothecary";
$OptLine[16] = "potions10 Herbalist dbBuildingApothecary";
$OptLine[17] = "potions11 Herbalist dbBuildingApothecary";
$OptLine[18] = "potions12 Herbalist dbBuildingApothecary";
$OptLine[19] = "potions13 Herbalist dbBuildingApothecary";
$OptLine[20] = "potions14 Herbalist dbBuildingApothecary";
$OptLine[21] = "potions15 Herbalist dbBuildingApothecary";
$OptLine[22] = "potions16 Herbalist dbBuildingApothecary";
$OptLine[23] = "potions17 Herbalist dbBuildingApothecary";
$OptLine[24] = "potions18 Herbalist dbBuildingApothecary";
// production sets whose items are scored as gear for the lord
$Opt::PSSets = "PS_Basic_Arms PS_Medium_Arms PS_Advanced_Weapons PS_Advanced_Armor PS_Wizard PS_Trinkets";
$Opt::PotionSets = "PS_Simple_Potions PS_Healing_Potions";

function OptBName(%bdb)
{
   if (!isObject(%bdb)) { return ""; }
   return getStringTableEntry(%bdb.StringIDName);
}

// gold a shown day a building makes with a worker of this bonus: its own gold (after the town's
// class bonus) plus what selling goods brings (Sell_Goods_Amount / 5 per game day), which only
// exists once the town has a Market and a Merchant
function OptBGold(%bdb, %pb)
{
   if (!isObject(%bdb)) { return 0; }
   %g = 0;
   if (%bdb.Gold_Produced > 0) { %g = (%bdb.Gold_Produced + %pb + gTown.goldProductionBonus) * $gDayApparentCycle; }
   return %g + (OptTownSells() ? %bdb.Sell_Goods_Amount : 0);
}

// gold a shown day the townsfolk would add by selling goods once a Market and a Merchant exist:
// each maker whose items are not worth making (the test the town chores use). the names go to
// $OptSellersNames
function OptSellersGold()
{
   %g = 0;
   $OptSellersNames = "";
   for (%i = 0; %i < TownsPeople.getCount(); %i++)
   {
      %p = TownsPeople.getObject(%i);
      if (!isObject(%p) || %p.getState() $= "Dead") { continue; }
      %b = %p.getHomeBuilding();
      if (!isObject(%b)) { continue; }
      %bdb = %b.getDataBlock();
      %set = %bdb.Production_Set;
      if (%bdb.Sell_Goods_Amount <= 0 || %set $= "" || !%b.canSell()) { continue; }
      if (strstr(" " @ $Opt::PSSets @ " ", " " @ %set @ " ") < 0) { continue; }
      if ($OptPSGain[%set] !$= "" && $OptPSGain[%set] <= -0.5) { continue; }
      %g += %bdb.Sell_Goods_Amount;
      $OptSellersNames = $OptSellersNames @ ($OptSellersNames $= "" ? "" : ", ") @ %p.getNameString();
   }
   return %g;
}

// what a Market with its Merchant brings a shown day: "gold|text"
function OptMarketWorth()
{
   %m = OptBGold(dbBuildingMarket, 0);
   %s = OptSellersGold();
   return (%m + %s) @ "|+" @ OptNum(%m) @ " gold a day" @ (%s > 0 ? ", +" @ OptNum(%s) @ " more from " @ $OptSellersNames @ " selling goods" : "") @ " · lets you sell spare gear";
}

// the first requirement a building still lacks: "house <db>", "access <resource>",
// "building <db>", or "" when it can be built now
function OptReqMissing(%bdb)
{
   for (%k = 1; %k <= 2; %k++)
   {
      %r = (%k == 1) ? %bdb.Required_Building_1 : %bdb.Required_Building_2;
      if (%r $= "" || %r $= "none" || !isObject(%r)) { continue; }
      %rn = %r.getName();
      if (gTown.HasBuilding(%rn)) { continue; }
      if (%rn $= "dbBuildingManor" || %rn $= "dbBuildingEstate" || %rn $= "dbBuildingKeep") { return "house " @ %rn; }
      return "building " @ %rn;
   }
   // BuildingData::meetsTownQualityRequirement (Market 1, Lab 3, Tower 5)
   if (%bdb.Town_Quality_Required > 0 && gTown.getTownQuality() < %bdb.Town_Quality_Required) { return "quality " @ %bdb.Town_Quality_Required; }
   for (%k = 1; %k <= 2; %k++)
   {
      %a = (%k == 1) ? %bdb.Required_Access_1 : %bdb.Required_Access_2;
      if (%a $= "" || !isObject(%a)) { continue; }
      if (gTown.getResourceAccess(%a.Name)) { continue; }
      return "access " @ %a.getName();
   }
   // an item the building uses up (BuildingData::meetsItemRequirement: the Tower a tome, the
   // Stage an instrument, the Tent an orb, the Dragon Ranch a dragon egg...): one from that set
   // must lie in the town inventory or your bag (PlayerTown::HasItemFromSet)
   for (%k = 1; %k <= 2; %k++)
   {
      %ci = (%k == 1) ? %bdb.Cost_Item_1 : %bdb.Cost_Item_2;
      if (%ci $= "" || %ci $= "none") { continue; }
      if (gTown.HasItemFromSet(%ci)) { continue; }
      return "item " @ %ci;
   }
   return "";
}

// an item set's name as plain words: "Set_Dragon_Eggs" > "dragon eggs"
function OptSetWords(%set)
{
   return strlwr(strreplace(strreplace(%set, "Set_", ""), "_", " "));
}

// the town upgrade (1 water, 2 herbs, 3 iron, 4 stone) that grants a resource, or 0
function OptAccessUpgrade(%res)
{
   for (%i = 1; %i <= 4; %i++)
   {
      if (isObject(gTown.UpgradeResource[%i]) && gTown.UpgradeResource[%i].getName() $= %res && !gTown.HasUpgrade(%i)) { return %i; }
   }
   return 0;
}

function OptAccessName(%i)
{
   return getWord("- Dig_Well Plant_Herb_Field Import_Iron Import_Stone", %i);
}

// the action that clears a missing requirement: "house|cost|title|info" or
// "access|cost|title|info", or "" when it cannot be bought (another line builds it, or a
// resource only the region gives)
function OptReqAction(%miss, %for)
{
   %kind = getWord(%miss, 0);
   %what = getWord(%miss, 1);
   if (%kind $= "house")
   {
      if (!isObject($manorHouse)) { return ""; }
      %cur = $manorHouse.getDataBlock();
      %up = %cur.Upgrades_To_1;
      if (!isObject(%up)) { return ""; }
      return "house|" @ %up.getUpgradeCost(%cur) @ "|Upgrade your house to " @ OptBName(%up) @ "|click your portrait (top right) and press Yes · needed for the " @ OptBName(%for);
   }
   if (%kind $= "access")
   {
      %i = OptAccessUpgrade(%what);
      if (%i < 1) { return ""; }
      return "access|" @ gTown.UpgradeCost[%i] @ "|Buy " @ strreplace(OptAccessName(%i), "_", " ") @ "|in your portrait window, Town Upgrades · needed for the " @ OptBName(%for);
   }
   // an item can't be bought: the line waits until one is found (chests, bodies)
   if (%kind $= "item")
   {
      return "wait|0|" @ OptBName(%for) @ " after you find a " @ OptSetWords(%what) @ "|it uses one up: keep it in your bag or the town inventory · from chests and bodies";
   }
   return "";
}

// gear change at the gear site if the lord wore an item of this datablock, or "" when not gear
function OptGearDeltaDb(%db)
{
   if (!isObject(%db) || %db.Useable) { return ""; }
   if (%db.category $= "Weapon" && %db.ActionType !$= "melee attack" && %db.ActionType !$= "missile attack") { return ""; }
   %L = $OptLord;
   %c = $OptObj["P", %L];
   if (!isObject(%c)) { return ""; }
   %slot = OptItemSlot(%c, %db);
   if (%slot $= "") { return ""; }
   %cur = OptEquipped(%c, %slot);
   %spd = %db.Speed + OptWeaponBonus(%c.getDataBlock(), %db);
   OptGDSwap(%db, 1);
   return OptGearDelta(%L, %c, %slot, %cur, %db.Attack, %db.Defense, %db.Health, %spd, %db.CritHitModifier);
}

// items already owned (worn, player inventory, town inventory) are not worth making again
function OptOwnedScan()
{
   $OptOwnedGen++;
   for (%s = 0; %s < 4; %s++)
   {
      %x = OptEquipped(MainCharacter, %s);
      if (isObject(%x)) { $OptOwned[%x.getDataBlock().getName()] = $OptOwnedGen; }
   }
   for (%v = 0; %v < 2; %v++)
   {
      %inv = (%v == 0) ? MainCharacter.getParty().GetInventory() : gTown.GetInventory();
      if (!isObject(%inv)) { continue; }
      %n = %inv.GetNumSlots();
      for (%s = 0; %s < %n; %s++)
      {
         %it = %inv.GetItem(%s);
         if (isObject(%it)) { $OptOwned[%it.getDataBlock().getName()] = $OptOwnedGen; }
      }
   }
}

// pipeline stage: the best gear change each production set could give (cached 30 s)
// RESEARCH TOO (the player: "you never considered craftsmen to research items, doesn't that
// increase chances of making better items?" then "yes, add them"). a building with a research
// set learns one random item of it per research and can make it from then on
// (completeItemResearch moves it from RS_x to PS_x). each gear production set's research set
// (RS_ for PS_, where the game has one: the Armory, the Weaponsmith, the Tower, the Hut) is
// scored the same way: the best gear change among the items still to learn
function OptPSAllSets()
{
   %all = "";
   for (%w = 0; %w < getWordCount($Opt::PSSets); %w++)
   {
      %set = getWord($Opt::PSSets, %w);
      %all = %all @ %set @ " ";
      %rs = "RS" @ getSubStr(%set, 2, 999);
      if (isObject(%rs) && isFunction("isItemResearchSet") && isItemResearchSet(%rs)) { %all = %all @ %rs @ " "; }
   }
   return trim(%all);
}

function OptPSBegin()
{
   OptOwnedScan();
   $OptPSn = 0;
   $OptPSI = 0;
   $OptPSAll = OptPSAllSets();
   for (%w = 0; %w < getWordCount($OptPSAll); %w++)
   {
      %set = getWord($OptPSAll, %w);
      $OptPSGainNew[%set] = 0;
      $OptPSBestNew[%set] = "";
      if (!isObject(%set)) { continue; }
      for (%i = 0; %i < %set.getCount(); %i++)
      {
         $OptPSSet[$OptPSn] = %set;
         $OptPSItem[$OptPSn] = %set.getObject(%i);
         $OptPSn++;
      }
   }
}

function OptPSStep(%t0)
{
   %done = 0;
   while ($OptPSI < $OptPSn)
   {
      if (!OptSliceLeft(%t0, %done, 6)) { return 0; }
      %i = $OptPSI;
      $OptPSI++;
      %done++;
      %db = $OptPSItem[%i];
      if (!isObject(%db) || $OptOwned[%db.getName()] == $OptOwnedGen) { continue; }
      %d = OptGearDeltaDb(%db);
      %set = $OptPSSet[%i];
      if (%d !$= "" && %d < $OptPSGainNew[%set]) { $OptPSGainNew[%set] = %d; $OptPSBestNew[%set] = %db.getNameString(); }
   }
   for (%w = 0; %w < getWordCount($OptPSAll); %w++)
   {
      %set = getWord($OptPSAll, %w);
      $OptPSGain[%set] = $OptPSGainNew[%set];
      $OptPSBest[%set] = $OptPSBestNew[%set];
   }
   $OptPSTime = getRealTime();
   return 1;
}

// best gear change from any production set along a line from stage %s on: "delta item" or "0"
function OptLineGearGain(%l, %s)
{
   %best = 0;
   %name = "";
   %n = getWordCount($OptLine[%l]) - 2;
   for (%k = %s; %k < %n; %k++)
   {
      %bdb = getWord($OptLine[%l], %k + 2);
      // what its research can still teach it counts too (OptPSAllSets)
      %rs = %bdb.Research_Set;
      if (%rs !$= "" && $OptPSGain[%rs] !$= "" && strstr(" " @ $OptPSAll @ " ", " " @ %rs @ " ") >= 0 && $OptPSGain[%rs] < %best) { %best = $OptPSGain[%rs]; %name = $OptPSBest[%rs] @ " (by research)"; }
      %set = %bdb.Production_Set;
      if (%set $= "" || $OptPSGain[%set] $= "") { continue; }
      if ($OptPSGain[%set] < %best) { %best = $OptPSGain[%set]; %name = $OptPSBest[%set]; }
   }
   return %best SPC %name;
}

// strongest bow a production set can make (attack)
function OptSetBestBow(%set)
{
   %b = 0;
   if (!isObject(%set)) { return 0; }
   for (%i = 0; %i < %set.getCount(); %i++)
   {
      %db = %set.getObject(%i);
      if (%db.ActionType $= "missile attack" && %db.Attack > %b) { %b = %db.Attack; }
   }
   return %b;
}

// which line each townsperson works for, and how far along it is
function OptTownScan()
{
   for (%l = 0; %l < $OptLineN; %l++) { $OptLW[%l] = 0; $OptLStage[%l] = -1; }
   $OptFarmers = 0;
   for (%i = 0; %i < TownsPeople.getCount(); %i++)
   {
      %p = TownsPeople.getObject(%i);
      if (!isObject(%p) || %p.getState() $= "Dead") { continue; }
      %b = %p.getHomeBuilding();
      if (!isObject(%b)) { continue; }
      %bn = %b.getDataBlock().getName();
      if (%b.getDataBlock().Food_Produced > 0) { $OptFarmers++; }
      // the first free line with this building; for a Craftsman before the weapons/armor fork,
      // whichever of the two leads to the better item
      %pick = -1;
      %pickK = -1;
      for (%l = 0; %l < $OptLineN && %pick < 0; %l++)
      {
         if (isObject($OptLW[%l])) { continue; }
         %n = getWordCount($OptLine[%l]) - 2;
         for (%k = 0; %k < %n; %k++)
         {
            if (getWord($OptLine[%l], %k + 2) $= %bn) { %pick = %l; %pickK = %k; break; }
         }
      }
      if (%pick < 0) { continue; }
      %key = getWord($OptLine[%pick], 0);
      if (%key $= "weapons" || %key $= "armor")
      {
         %other = OptLineIndex(%key $= "weapons" ? "armor" : "weapons");
         if (%other >= 0 && !isObject($OptLW[%other]) && getWord($OptLine[%other], %pickK + 2) $= %bn)
         {
            %gMine = getWord(OptLineGearGain(%pick, %pickK + 1), 0);
            %gOther = getWord(OptLineGearGain(%other, %pickK + 1), 0);
            if (%gOther < %gMine) { %pick = %other; }
         }
      }
      $OptLW[%pick] = %p;
      $OptLStage[%pick] = %pickK;
   }
}

// the index of the town plan line with this key, or -1
function OptLineIndex(%key)
{
   for (%l = 0; %l < $OptLineN; %l++) { if (getWord($OptLine[%l], 0) $= %key) { return %l; } }
   return -1;
}

// a visitor of this profession waiting in the Visitors panel, or 0
function OptVisitorOf(%prof)
{
   for (%i = 0; %i < $MaxNumberOfCandidates; %i++)
   {
      %c = gTown.AvailableCandidates[%i];
      // a visitor on 0 days left is still there and hireable until the next day (replaceCandidates)
      if (isObject(%c) && %c.myCandidateData.Profession $= %prof && gTown.CandidateTimeRemaining[%i] >= 0) { return %c; }
   }
   return 0;
}

// how many first stages of line %l another line of the same profession already covers with its
// own worker. two Craftsmen both start at a Workshop and pass a Smithy: the items there come from
// the first one, only the stages after the shared part are new. a line without a worker is
// measured against every staffed line, a staffed one only against staffed lines above it (so
// the shared stages count for exactly one of them)
function OptLineShared(%l)
{
   %prof = getWord($OptLine[%l], 1);
   %n = getWordCount($OptLine[%l]) - 2;
   %u = 0;
   %mine = isObject($OptLW[%l]);
   for (%o = 0; %o < $OptLineN; %o++)
   {
      if (%o == %l || (%mine && %o > %l)) { continue; }
      if (!isObject($OptLW[%o]) || getWord($OptLine[%o], 1) !$= %prof) { continue; }
      %m = getWordCount($OptLine[%o]) - 2;
      %k = 0;
      while (%k < %n && %k < %m && getWord($OptLine[%l], %k + 2) $= getWord($OptLine[%o], %k + 2)) { %k++; }
      if (%k > %u) { %u = %k; }
   }
   return %u;
}

// the first stage of line %l that gives something by itself: the Guard House only leads to the
// Guard Tower (a Guard set to hide does nothing), the Workshop of the bows line only leads to the
// Fletchery. 0 for every other line
function OptLineValueStage(%l)
{
   %key = getWord($OptLine[%l], 0);
   if (strstr(%key, "defense") == 0 || %key $= "bows") { return getWordCount($OptLine[%l]) - 3; }
   return 0;
}

// THE INFIRMARY ONLY FOR ANOTHER LINE (the player's rule, see the potions line). nothing in the
// game needs the Infirmary itself: no building names it as a requirement and nothing upgrades
// from it. what it does give other lines is town quality: its Healthcare access is +2
// (PlayerTown::onResourceChanged adds the access's QualityBonus), and the Lab needs town
// quality 3, the Wizard's Tower 5. returns the name of the building another line wants next
// that lacks nothing but town quality the Infirmary would bring (its houses and buildings are
// there, and the accesses it still has to buy would not bring enough), or ""
function OptInfirmaryFor()
{
   %add = dbBuildingInfirmary.Grants_Access.QualityBonus;
   if (%add <= 0) { return ""; }
   %q = gTown.getTownQuality();
   for (%l = 0; %l < $OptLineN; %l++)
   {
      if (getWord($OptLine[%l], 0) $= "potions") { continue; }
      %w = $OptLW[%l];
      %s = isObject(%w) ? $OptLStage[%l] + 1 : 0;
      if (%s > getWordCount($OptLine[%l]) - 3) { continue; }
      %b = getWord($OptLine[%l], %s + 2);
      %need = %b.Town_Quality_Required;
      if (%need <= %q || getWord(OptReqMissing(%b), 0) !$= "quality") { continue; }
      %qa = 0;
      for (%k = 1; %k <= 2; %k++)
      {
         %a = (%k == 1) ? %b.Required_Access_1 : %b.Required_Access_2;
         if (%a !$= "" && isObject(%a) && !gTown.getResourceAccess(%a.Name)) { %qa += %a.QualityBonus; }
      }
      if (%need <= %q + %qa || %need > %q + %qa + %add) { continue; }
      %v = strreplace(OptLineWorth(%l, %s, isObject(%w) ? %w.productionBonus : 0), "|", "\t");
      if (!getField(%v, 0)) { continue; }
      return OptBName(%b);
   }
   return "";
}

// BUILDING LOTS: every townsperson lives in a building on one of the town's lots (the basic town
// has 28, gTown.numValidLots, lots 0 to lastBuildableLot; the house, the mine and the portals
// stand on their own spots). each town building keeps its lot in lotIndex. the lots still free
function OptFreeLots()
{
   %last = gTown.getDataBlock().lastBuildableLot;
   if (%last $= "") { %last = gTown.numValidLots - 1; }
   %used = 0;
   for (%i = 0; %i < TownBuildingsGroup.getCount(); %i++)
   {
      %b = TownBuildingsGroup.getObject(%i);
      if (!isObject(%b) || %b.lotIndex $= "") { continue; }
      if (%b.lotIndex >= 0 && %b.lotIndex <= %last) { %used++; }
   }
   return OptMax(gTown.numValidLots - %used, 0);
}

// 1 when hiring someone of this profession needs a new building (no empty one of his kind)
function OptNeedsLot(%prof)
{
   %empty = getEmptyFollowerBuildings(%prof);
   %n = %empty.getCount();
   %empty.delete();
   return %n < 1;
}

// whether stage %s of line %l is worth having, and what it gives: "worth|gold a day|info"
function OptLineWorth(%l, %s, %pb)
{
   %key = getWord($OptLine[%l], 0);
   %bdb = getWord($OptLine[%l], %s + 2);
   %prev = (%s > 0) ? getWord($OptLine[%l], %s + 1) : "";
   %dg = OptBGold(%bdb, %pb) - (isObject(%prev) ? OptBGold(%prev, %pb) : 0);
   if (%key $= "market")
   {
      %mw = strreplace(OptMarketWorth(), "|", "\t");
      return (getField(%mw, 0) > 0) @ "|" @ getField(%mw, 0) @ "|" @ getField(%mw, 1);
   }
   if (%key $= "gold" || (%key $= "fortune" && %s == 0))
   {
      if (%bdb.getName() $= "dbBuildingInn" && !isObject($OptLW[1]))
      {
         // the Inn is what the Market waits for (with the Manor and town quality 1, which the
         // Inn's Relaxation access gives)
         %mw = strreplace(OptMarketWorth(), "|", "\t");
         return "1|" @ (%dg + getField(%mw, 0)) @ "|unlocks the Market and its Merchant (" @ getField(%mw, 1) @ ") · 2 visitor slots";
      }
      // the visitors at a time it adds (Candidate_Bonus): worth it for them while Farmers are short
      %vb = %bdb.Candidate_Bonus - (isObject(%prev) ? %prev.Candidate_Bonus : 0);
      %vt = (%vb > 0) ? " · +" @ %vb @ " visitor" @ (%vb == 1 ? "" : "s") @ " at a time" : "";
      return (%dg > 0 || (%vb > 0 && OptFarmersShort())) @ "|" @ %dg @ "|+" @ OptNum(%dg) @ " gold a day" @ %vt;
   }
   if (%key $= "potions")
   {
      if (%s == 0) { return "1|" @ %dg @ "|makes healing potions"; }
      // the Infirmary: only for another line's town quality (OptInfirmaryFor), never for itself
      %for = OptInfirmaryFor();
      if (%for $= "") { return "0|0|"; }
      return "1|0|its Healthcare brings the town quality the " @ %for @ " needs (+" @ dbBuildingInfirmary.Grants_Access.QualityBonus @ ")";
   }
   // an extra Herbalist kept as an Apothecary: always wanted. no gold counted, he makes potions
   // instead of selling goods, so the hire is never taken ahead as a quick payback
   if (strstr(%key, "potions") == 0) { return "1|0|makes healing potions only, stays an Apothecary · +" @ dbBuildingApothecary.Town_Healing_Bonus @ " HP every 24 s resting in town"; }
   // every Guard line, whenever raiders come (the player's endgame town: towers against raids,
   // his hidden Farmer died in one; it used to count only while Guardian was still open)
   if (strstr(%key, "defense") == 0)
   {
      if (!MainCharacter.raidersEnabled) { return "0|0|"; }
      return "1|0|" @ (%s == 0 ? "first step to the Guard Tower" : "tower shoots raiders (attack 20), keeps townsfolk alive");
   }
   if (%key $= "bows")
   {
      // worth only when the Fletchery's bows beat the best bow you own (a Trapper's Hunting Lodge
      // no longer counts: Farmers only, #162)
      if (!OptNeed($Achievement_David)) { return "0|0|"; }
      %have = getWord(OptBestBow(), 0) + 0;
      %n = getWordCount($OptLine[%l]) - 2;
      %can = OptSetBestBow(getWord($OptLine[%l], %n + 1).Production_Set);
      if (%can <= %have) { return "0|0|"; }
      return "1|" @ %dg @ "|" @ (%s < %n - 1 ? "first step to the Fletchery: " : "makes ") @ "bows up to attack " @ %can @ " for David (yours: " @ %have @ ")";
   }
   // gear lines: worth if this stage or a later one makes an item that lowers your damage taken.
   // stages an earlier line's worker of the same profession already reaches don't count here
   %u = OptLineShared(%l);
   %g = OptLineGearGain(%l, OptMax(%s, %u));
   %gd = getWord(%g, 0);
   if (%gd <= -0.5)
   {
      %own = %bdb.Production_Set;
      %here = %s >= %u && (%own !$= "" && $OptPSGain[%own] !$= "" && $OptPSGain[%own] <= -0.5);
      return "1|" @ %dg @ "|" @ (%here ? "makes " : "leads to ") @ getWords(%g, 1) @ " (" @ OptNum(%gd) @ " damage taken)";
   }
   // the first stage still pays as a seller of goods
   if (%s == 0 && %dg > 0) { return "1|" @ %dg @ "|sells goods: +" @ OptNum(%dg) @ " gold a day"; }
   return "0|0|";
}

// the next action on line %l, dropped when it is a gold step that would not pay back within
// $Opt::WorthPayback shown days
function OptLineAction(%l)
{
   %a = OptLineActionRaw(%l);
   %f = strreplace(%a, "|", "\t");
   %kind = getField(%f, 0);
   if (%kind $= "none" || %kind $= "wait") { return %a; }
   %key = getWord($OptLine[%l], 0);
   %stage = isObject($OptLW[%l]) ? $OptLStage[%l] + 1 : 0;
   %goldStep = (%key $= "gold" || %key $= "market" || (%key $= "fortune" && %stage == 0));
   if (%goldStep && getField(%f, 5) > $Opt::WorthPayback && !(%key $= "gold" && OptFarmersShort())) { return "none"; }
   return %a;
}

// the next action on line %l: "kind|cost|title|info|target|payback", kind hire, upgrade, house,
// access, wait (no visitor of that kind yet) or none
function OptLineActionRaw(%l)
{
   %prof = getWord($OptLine[%l], 1);
   %n = getWordCount($OptLine[%l]) - 2;
   %w = $OptLW[%l];
   %town = gTown.getTownCenter();
   if (isObject(%w))
   {
      %s = $OptLStage[%l];
      if (%s >= %n - 1) { return "none"; }
      %cur = getWord($OptLine[%l], %s + 2);
      %next = getWord($OptLine[%l], %s + 3);
      %v = strreplace(OptLineWorth(%l, %s + 1, %w.productionBonus), "|", "\t");
      if (!getField(%v, 0)) { return "none"; }
      %cost = %next.getUpgradeCost(%cur);
      %dg = getField(%v, 1);
      %miss = OptReqMissing(%next);
      if (%miss !$= "")
      {
         %ra = OptReqAction(%miss, %next);
         if (%ra $= "") { return "none"; }
         %rc = getField(strreplace(%ra, "|", "\t"), 1);
         return %ra @ "|" @ %town @ "|" @ (%dg > 0 ? (%rc + %cost) / %dg : 9999);
      }
      return "upgrade|" @ %cost @ "|Upgrade " @ %w.getNameString() @ "'s " @ OptBName(%cur) @ " to " @ OptBName(%next) @ "|click " @ %w.getNameString() @ " and press the upgrade button · " @ getField(%v, 2) @ "|" @ %w.getHomeBuilding().getPosition() @ "|" @ (%dg > 0 ? %cost / %dg : 9999);
   }
   %base = getWord($OptLine[%l], 2);
   %v = strreplace(OptLineWorth(%l, 0, 0), "|", "\t");
   if (!getField(%v, 0)) { return "none"; }
   %dg = getField(%v, 1);
   %miss = OptReqMissing(%base);
   if (%miss !$= "")
   {
      %ra = OptReqAction(%miss, %base);
      if (%ra $= "") { return "none"; }
      %rc = getField(strreplace(%ra, "|", "\t"), 1);
      return %ra @ "|" @ %town @ "|" @ (%dg > 0 ? (%rc + %base.Cost_Gold) / %dg : 9999);
   }
   // a worker whose value starts at a later building (a second Craftsman for the Armory, a Guard
   // for the Guard Tower, a Craftsman for the Fletchery) is only hired once every building on his
   // way can be built: until then he makes what others make, or hides, and eats
   %u = OptMax(OptLineShared(%l), OptLineValueStage(%l));
   for (%k = 1; %k <= %u && %k < getWordCount($OptLine[%l]) - 2; %k++)
   {
      %sb = getWord($OptLine[%l], %k + 2);
      %miss = OptReqMissing(%sb);
      if (%miss $= "") { continue; }
      %ra = OptReqAction(%miss, %sb);
      %first = (%ra !$= "") ? getField(strreplace(%ra, "|", "\t"), 2) : "the " @ OptBName(%sb) @ " requirements";
      return "wait|0|Hire a " @ %prof @ " after: " @ %first @ "|" @ getField(%v, 2) @ "||9999";
   }
   // the first hire is a food worker
   if (!OptHasFoodWorker() && !(getWord($OptLine[%l], 0) $= "gold" && OptFarmersShort() && OptGoodmanFoodOk())) { return "wait|0|Hire a " @ %prof @ " after your first Farmer|" @ getField(%v, 2) @ "||9999"; }
   // every building lot taken: nobody who needs a new building
   if (OptNeedsLot(%prof) && OptFreeLots() < 1) { return "wait|0|Hire a " @ %prof @ " when a building lot is free (all " @ gTown.numValidLots @ " are taken)|" @ getField(%v, 2) @ "||9999"; }
   %c = OptVisitorOf(%prof);
   if (!isObject(%c)) { return "wait|0|Hire a " @ %prof @ " when one visits|" @ getField(%v, 2) @ "||9999"; }
   %info = strreplace(OptHireInfo(%c), "|", "\t");
   %cost = getField(%info, 1);
   return "hire|" @ %cost @ "|Hire the " @ %c.getProfessionName() @ " (Visitors, top left)|" @ getField(%info, 2) @ " · " @ getField(%v, 2) @ (OptNeedGuardian() ? " · then make sure " @ OptHis(%c) @ " window says \"" @ getStringTableEntry($ID_TOWNS_PERSON_DEFEND) @ "\"" : "") @ "|" @ %town @ "|" @ (%dg > 0 ? %cost / %dg : 9999);
}

// the town's next move: "do|<action>" when it can be made now, "save|<action>" when gold is
// short for it, or "". gold that pays back within $Opt::QuickPayback days comes first; then the
// lines in order. a new mouth is only hired while the farms can feed it
function OptTownPick()
{
   OptTownScan();
   %gold = gTown.Gold;
   %foodOk = OptFoodNetPlanned() >= 1;
   $OptTownPickKeep = 0;
   // pass 1: quick paybacks
   %best = "";
   %bestP = $Opt::QuickPayback;
   for (%l = 0; %l < $OptLineN; %l++)
   {
      %a = OptLineAction(%l);
      %f = strreplace(%a, "|", "\t");
      %kind = getField(%f, 0);
      if (%kind $= "none" || %kind $= "wait") { continue; }
      if (%kind $= "hire" && !%foodOk && !(getWord($OptLine[%l], 0) $= "gold" && OptFarmersShort() && OptGoodmanFoodOk())) { continue; }
      %p = getField(%f, 5);
      if (%p <= %bestP && getField(%f, 1) <= %gold) { %best = %a; %bestP = %p; }
   }
   if (%best !$= "") { return "do|" @ %best; }
   // pass 2: line order. while the Market route waits for a visitor, the lines after it only get
   // the gold above what that route still costs
   $OptTownReserve = OptMarketReserve();
   for (%l = 0; %l < $OptLineN; %l++)
   {
      %a = OptLineAction(%l);
      %f = strreplace(%a, "|", "\t");
      %kind = getField(%f, 0);
      if (%kind $= "none" || %kind $= "wait") { continue; }
      if (%kind $= "hire" && !%foodOk && !(getWord($OptLine[%l], 0) $= "gold" && OptFarmersShort() && OptGoodmanFoodOk())) { continue; }
      %keep = OptLineIsMarketRoute(%l) ? 0 : $OptTownReserve;
      // what the town line and the lord's panel show as kept back
      $OptTownPickKeep = %keep;
      if (getField(%f, 1) <= %gold - %keep) { return "do|" @ %a; }
      return "save|" @ %a;
   }
   return "";
}

function OptLineIsMarketRoute(%l)
{
   %key = getWord($OptLine[%l], 0);
   return %key $= "gold" || %key $= "market";
}

// the expected price of hiring a visitor of this profession with his home: the exact price when
// one is in town, else the game's own price at the middle of today's level range
// (AddCandidate: level getRandom(getMinCandidateLevel, getMaxCandidateLevel), price
// CandidateData::getCost, home Cost_Gold x BuildCostMultiplier)
function OptVisitorPrice(%prof, %cdb, %bdb)
{
   %home = mFloor(%bdb.Cost_Gold * gTown.BuildCostMultiplier);
   %c = OptVisitorOf(%prof);
   if (isObject(%c)) { return %c.HireCost + %home; }
   if (!isObject(%cdb)) { return %home; }
   %lvl = 0;
   if (isFunction("getMinCandidateLevel") && isFunction("getMaxCandidateLevel")) { %lvl = (getMinCandidateLevel() + getMaxCandidateLevel()) / 2; }
   return mCeil(%cdb.getCost(%lvl)) + %home;
}

// gold the Market route still costs (house to Manor, Goodman with his Hostel, Hostel to Inn,
// Merchant with his Market): kept back from every other line while the route waits for a
// visitor. it pays (Inn 1 + Market 2 + the class gold bonus per game day) x 5 a shown day, 13 for
// a Knight, and lets you sell gear, so it comes before buys that make no gold. 0 once the Market
// has its Merchant
function OptMarketReserve()
{
   if (gTown.HasOccupiedBuilding(dbBuildingMarket) || gTown.HasOccupiedBuilding(dbBuildingOrcMarket)) { return 0; }
   %r = 0;
   if (!OptHasManor()) { %r += OptManorCost(); }
   %gl = OptLineIndex("gold");
   %w = (%gl >= 0) ? $OptLW[%gl] : 0;
   if (!isObject(%w))
   {
      %r += OptVisitorPrice("Goodman", dbCandidateGoodman, dbBuildingHostel);
      %r += dbBuildingInn.getUpgradeCost(dbBuildingHostel);
   }
   else if ($OptLStage[%gl] == 0) { %r += dbBuildingInn.getUpgradeCost(dbBuildingHostel); }
   %r += OptVisitorPrice("Merchant", dbCandidateMerchant, dbBuildingMarket);
   return %r;
}

// 1 when every hire the Market route still needs has its visitor in town now: the Goodman while
// the gold line has no worker, and the Merchant while the Market has none. a visitor held back
// for that route only stays while it is so; otherwise his visitor slot is freed for theirs
function OptMarketRouteHere()
{
   if (gTown.HasOccupiedBuilding(dbBuildingMarket) || gTown.HasOccupiedBuilding(dbBuildingOrcMarket)) { return 1; }
   if (!isObject(OptVisitorOf("Merchant"))) { return 0; }
   %gl = OptLineIndex("gold");
   OptTownScan();
   if (%gl >= 0 && !isObject($OptLW[%gl]) && !isObject(OptVisitorOf("Goodman"))) { return 0; }
   return 1;
}

// a visitor's place in the town plan right now, lower is hired first; "" when not wanted. the
// same order the plan itself uses: the farm worker the food route asks for, then hires that pay
// back within $Opt::QuickPayback days (quickest first), then the plan's lines in order
function OptVisitorRank(%o)
{
   %best = OptBestFarmHire();
   if ((OptFoodNetPlanned() < 1 || OptFoodShort()) && isObject(%best) && %best.getId() == %o.getId()) { return 0; }
   if (OptFarmerWanted(%o)) { return 1; }
   %keepInfo = $OptWantInfo;
   %keepLater = $OptWantLater;
   %l = OptTownWants(%o);
   %r = "";
   if (%l >= 0)
   {
      %p = getField(strreplace(OptLineAction(%l), "|", "\t"), 5);
      %r = (%p !$= "" && %p <= $Opt::QuickPayback) ? 100 + %p : 1000 + %l;
   }
   $OptWantInfo = %keepInfo;
   $OptWantLater = %keepLater;
   return %r;
}

// another visitor in town ranked above %c, hireable on its own, when the gold can't pay for
// both: "profession<TAB>cost", or ""
function OptVisitorAhead(%c, %costC)
{
   %rc = OptVisitorRank(%c);
   if (%rc $= "") { return ""; }
   for (%i = 0; %i < $MaxNumberOfCandidates; %i++)
   {
      %o = gTown.AvailableCandidates[%i];
      if (!isObject(%o) || %o.getId() == %c.getId() || gTown.CandidateTimeRemaining[%i] < 0) { continue; }
      %ro = OptVisitorRank(%o);
      if (%ro $= "" || %ro >= %rc) { continue; }
      %co = getField(strreplace(OptHireInfo(%o), "|", "\t"), 1);
      if (%co <= gTown.Gold && gTown.Fame >= %o.FameRequired && %co + %costC > gTown.Gold) { return %o.getProfessionName() TAB %co; }
   }
   return "";
}

// the profession of another visitor in town the plan wants to hire now (not %c), or ""
function OptOtherWantedVisitor(%c)
{
   %keepInfo = $OptWantInfo;
   %keepLater = $OptWantLater;
   %out = "";
   for (%i = 0; %i < $MaxNumberOfCandidates && %out $= ""; %i++)
   {
      %o = gTown.AvailableCandidates[%i];
      if (!isObject(%o) || %o.getId() == %c.getId() || gTown.CandidateTimeRemaining[%i] < 0) { continue; }
      if (OptTownWants(%o) >= 0) { %out = %o.getProfessionName(); }
   }
   $OptWantInfo = %keepInfo;
   $OptWantLater = %keepLater;
   return %out;
}

// 1 when %c is the NEXT townsperson the plan buys: the first food worker while you have none,
// the farm worker the food route asks for while the farms can't feed one more, else the
// plan's hires in its own order (the lines: Goodman, Merchant, Craftsman, Herbalist...),
// whether a visitor of that kind is in town or not. the next hire is the first line still
// needing one; when its visitor is in town the line after it is next too (both can be bought
// one after the other), and so on, until a hire whose visitor is not in town. a Craftsman
// visiting while the plan's next hire is a Goodman nobody brought is not next
function OptIsNextHire(%c)
{
   %keepInfo = $OptWantInfo;
   %keepLater = $OptWantLater;
   %best = OptBestFarmHire();
   if (!OptHasFoodWorker())
   {
      %r = OptMakesFood(%c) && (!isObject(%best) || %best.getId() == %c.getId() || !OptMakesFood(%best));
   }
   else if (OptFarmerWanted(%c))
   {
      // FOOD WORKERS UP TO 6 (OptFoodShort): a food worker who visits is next, unless the last
      // place goes to a better one hireable now
      %r = !isObject(%best) || %best.getId() == %c.getId() || OptFoodWorkers() + 1 < $Opt::Farmers;
   }
   else if (OptFoodNetPlanned() < 1)
   {
      %r = isObject(%best) && %best.getId() == %c.getId();
   }
   else
   {
      OptTownScan();
      %prof = %c.myCandidateData.Profession;
      %r = 0;
      for (%l = 0; %l < $OptLineN; %l++)
      {
         %f = strreplace(OptLineAction(%l), "|", "\t");
         %kind = getField(%f, 0);
         if (%kind $= "hire")
         {
            // this line's visitor is in town: %c is next if it is this one, else the chain goes on
            if (getWord($OptLine[%l], 1) $= %prof) { %r = 1; break; }
            continue;
         }
         // the next hire in the plan has nobody in town: nothing after it is next
         if (%kind $= "wait" && strstr(getField(%f, 2), " when one visits") >= 0) { break; }
      }
   }
   $OptWantInfo = %keepInfo;
   $OptWantLater = %keepLater;
   return %r;
}

// what is missing to hire %c for %cost gold: "N gold short", "N fame short", or both
function OptShortText(%c, %cost)
{
   %g = %cost - mFloor(gTown.Gold);
   %f = %c.FameRequired - mFloor(gTown.Fame);
   %t = (%g > 0) ? %g @ " gold short" : "";
   if (%f > 0) { %t = %t @ (%t $= "" ? "" : ", ") @ %f @ " fame short"; }
   return (%t $= "") ? "not affordable yet" : %t;
}

// does the town plan want to hire this visitor now? its line index, or -1
function OptTownWants(%c)
{
   %prof = %c.myCandidateData.Profession;
   OptTownScan();
   // $OptWantLater: why a line that wants this profession can't use one yet, or ""
   $OptWantLater = "";
   for (%l = 0; %l < $OptLineN; %l++)
   {
      if (isObject($OptLW[%l]) || getWord($OptLine[%l], 1) !$= %prof) { continue; }
      %f = strreplace(OptLineAction(%l), "|", "\t");
      if (getField(%f, 0) $= "hire") { $OptWantInfo = getField(%f, 3); return %l; }
      if (getField(%f, 0) $= "wait" && strstr(getField(%f, 2), " after: ") >= 0 && $OptWantLater $= "") { $OptWantLater = getField(%f, 2) @ " (" @ getField(%f, 3) @ ")"; }
   }
   return -1;
}

// details view: every line of the town plan, where it stands and its next step
function OptTownText()
{
   OptTownScan();
   %t = $Opt::Gold @ "TOWN PLAN" @ $Opt::White @ "  food " @ ((OptFoodNetPlanned() >= 0) ? "+" : "") @ OptNum(OptFoodNetPlanned()) @ "/day once farms are upgraded\n";
   for (%l = 0; %l < $OptLineN; %l++)
   {
      %key = getWord($OptLine[%l], 0);
      %w = $OptLW[%l];
      %have = isObject(%w) ? OptBName(getWord($OptLine[%l], $OptLStage[%l] + 2)) : "-";
      %f = strreplace(OptLineAction(%l), "|", "\t");
      %kind = getField(%f, 0);
      if (%kind $= "none") { %next = isObject(%w) && $OptLStage[%l] >= getWordCount($OptLine[%l]) - 3 ? "done" : "not worth it now"; }
      else if (%kind $= "wait") { %next = getField(%f, 2); }
      else { %next = getField(%f, 3) @ " (" @ getField(%f, 2) @ " gold)"; }
      %t = %t @ "  " @ %key @ ": " @ %have @ " > " @ %next @ "\n";
   }
   return %t;
}

// one line on the town plan: the next move, or what gold is being saved for
function OptTownLine()
{
   if ($OptNowTown) { return ""; }
   if ($OptTownTP $= "") { $OptTownTP = OptTownPick(); }
   if ($OptTownTP $= "") { return ""; }
   %f = strreplace($OptTownTP, "|", "\t");
   if (getField(%f, 0) $= "save")
   {
      %rk = $OptTownPickKeep;
      if (%rk > 0) { return $Opt::Grey @ "Town: keeping " @ %rk @ " gold for the Market route (Goodman, Inn, Market, Merchant), then " @ getField(%f, 3) @ " (" @ mFloor(gTown.Gold) @ "/" @ (%rk + getField(%f, 2)) @ " gold)" @ $Opt::White @ "\n"; }
      return $Opt::Grey @ "Town: saving for " @ getField(%f, 3) @ " (" @ mFloor(gTown.Gold) @ "/" @ getField(%f, 2) @ " gold)" @ $Opt::White @ "\n";
   }
   return $Opt::Grey @ "Town: next " @ getField(%f, 3) @ " (" @ getField(%f, 2) @ " gold)" @ $Opt::White @ "\n";
}

// one-time clicks in town: Hide for every townsperson while Guardian is open, and each maker set
// to make items when they are worth having, or to sell goods when not. sets the step, returns 1
function OptTownChores()
{
   if (OptHideChore()) { return 1; }
   if (OptSwapChore()) { return 1; }
   if ($OptPSTime $= "") { return 0; }
   for (%i = 0; %i < TownsPeople.getCount(); %i++)
   {
      %p = TownsPeople.getObject(%i);
      if (!isObject(%p) || %p.getState() $= "Dead") { continue; }
      %b = %p.getHomeBuilding();
      if (!isObject(%b)) { continue; }
      %bdb = %b.getDataBlock();
      %set = %bdb.Production_Set;
      if (%set $= "") { continue; }
      // RESEARCH WHEN SOMETHING STILL TO LEARN BEATS ALL IT MAKES (OptPSAllSets): the best gear
      // change among the items its research set still holds, at least 0.5 better than the best
      // it can make now. it learns one random item of them per research, so it keeps researching
      // while that is so; then it is set back to make items (the make / sell rules below)
      %rs = %bdb.Research_Set;
      %hasRS = %rs !$= "" && isObject(%rs) && strstr(" " @ $OptPSAll @ " ", " " @ %rs @ " ") >= 0;
      if (%hasRS && $OptPSGain[%rs] $= "") { continue; }
      %pg = ($OptPSGain[%set] $= "") ? 0 : $OptPSGain[%set];
      %research = %hasRS && %b.canResearch() && $OptPSGain[%rs] <= -0.5 && $OptPSGain[%rs] < %pg - 0.5;
      if (%b.occupantActionType $= "Researching")
      {
         // the game itself researches when there is nothing to make yet (pickSomethingToDo)
         if (%research || !%hasRS || !%b.canProduce()) { continue; }
         OptNow("Set " @ %p.getNameString() @ " to make items", "click " @ OptHim(%p) @ " and press " @ getStringTableEntry($ID_TOWNS_PERSON_PRODUCE_ITEMS) @ " (it shows what " @ OptHe(%p) @ " does now) · nothing left to learn beats what " @ OptHe(%p) @ " can make", "");
         return 1;
      }
      if (%research)
      {
         %rtxt = OptHis(%p) @ " " @ %b.getNameString() @ " can learn " @ $OptPSBest[%rs] @ " (" @ OptNum($OptPSGain[%rs]) @ " damage taken), better than anything " @ OptHe(%p) @ " makes now" @ (%pg <= -0.5 ? " (" @ $OptPSBest[%set] @ ", " @ OptNum(%pg) @ ")" : "") @ " · each research teaches one random item of the " @ %rs.getCount() @ " still to learn, and nothing is made meanwhile";
         if (%b.occupantActionType $= "Selling")
         {
            OptNow("Set " @ %p.getNameString() @ " to make items", "click " @ OptHim(%p) @ " and press " @ getStringTableEntry($ID_TOWNS_PERSON_PRODUCE_GOODS) @ " (it shows what " @ OptHe(%p) @ " does now), then " @ getStringTableEntry($ID_TOWNS_PERSON_RESEARCH_ITEMS) @ " · " @ %rtxt, "");
            return 1;
         }
         if (%b.occupantActionType $= "Producing")
         {
            OptNow("Set " @ %p.getNameString() @ " to research items", "click " @ OptHim(%p) @ " and press " @ getStringTableEntry($ID_TOWNS_PERSON_RESEARCH_ITEMS) @ " · " @ %rtxt, "");
            return 1;
         }
         continue;
      }
      %potion = strstr(" " @ $Opt::PotionSets @ " ", " " @ %set @ " ") >= 0;
      %gain = $OptPSGain[%set] !$= "" && $OptPSGain[%set] <= -0.5;
      %make = %potion || %gain;
      if (%make && %b.occupantActionType $= "Selling")
      {
         OptNow("Set " @ %p.getNameString() @ " to make items", "click " @ OptHim(%p) @ " and press " @ getStringTableEntry($ID_TOWNS_PERSON_PRODUCE_GOODS) @ " (it shows what " @ OptHe(%p) @ " does now) · " @ (%potion ? "healing potions" : $OptPSBest[%set] @ " (" @ OptNum($OptPSGain[%set]) @ " damage taken)"), "");
         return 1;
      }
      if (!%make && %bdb.Sell_Goods_Amount > 0 && %b.occupantActionType !$= "Selling" && (strstr(" " @ $Opt::PSSets @ " ", " " @ %set @ " ") >= 0) && OptCanSellGoods(%b))
      {
         OptNow("Set " @ %p.getNameString() @ " to sell goods", "click " @ OptHim(%p) @ " and press " @ getStringTableEntry($ID_TOWNS_PERSON_SELL_GOODS) @ " (it shows what " @ OptHe(%p) @ " does now) · nothing " @ OptHe(%p) @ " makes beats your gear · +" @ %bdb.Sell_Goods_Amount @ " gold a day", "");
         return 1;
      }
   }
   return 0;
}

// the townsperson window only offers selling goods when the building can sell, the town has a
// Market, and a Merchant lives in town or travels in your party (EnableProfessionSpecificOptions)
function OptCanSellGoods(%b)
{
   return %b.canSell() && OptTownSells();
}

// a Market and a Merchant (in town or in your party): makers can sell goods
function OptTownSells()
{
   if (!(gTown.HasBuilding(dbBuildingMarket) || gTown.HasBuilding(dbBuildingOrcMarket))) { return 0; }
   for (%i = 0; %i < TownsPeople.getCount(); %i++)
   {
      %prof = TownsPeople.getObject(%i).myCandidateData.Profession;
      if (%prof $= "Merchant" || %prof $= "OrcMerchant") { return 1; }
   }
   %party = MainCharacter.getParty();
   for (%i = 0; %i < %party.getPartySize(); %i++)
   {
      %prof = %party.getMember(%i).myCandidateData.Profession;
      if (%prof $= "Merchant" || %prof $= "OrcMerchant") { return 1; }
   }
   return 0;
}

// SWAP FOR A HIGHER LEVEL (the player's rule: "if a trapper with a higher level than my current
// one appears ... remove the lower one and hire the higher level one", for more HP and defense
// against raiders: his level 1 Farmer was set to hide and still died in a raid). a worker's
// level changes nothing he makes (a building adds only his items' bonus), only his attack,
// defense and HP. a visitor and a townsperson of the SAME profession whose home the visitor can
// move into (its Occupant is that profession: the base building, the Large Farm, the
// Plantation; never an Inn, Smithy or other upgrade, which no visitor fills), the visitor's
// shown level higher. the visitor is still in town (his last day too: the player had the gold
// and was told "not affordable yet" on that day) with his fame met, and his hire price (the
// building stays, so no building price) must fit in your gold above what the Market route
// keeps. the townsperson must be at full HP (sending away an injured one costs fame,
// Creature::evictFromTown) and not in your party. the biggest level gain wins.
// HELD WHILE THE JOB IS NOT FULL (the player's rule: "we fill the 6 farmers first, then care
// about levels of townsfolk later"): no swap while the plan still wants more of that profession
// (OptJobFull); the visitor is hired as one more instead.
// FOOD WORKERS TRADED UP (the player's rule: "let's say we have 4 farmers, 2 herders: a trapper
// appears - we get one herder out - we put the trapper in ... until we have 6 farmers"): with the
// plan's 6 food workers in town (OptFoodShort false) and fewer than 6 Farmers, a visiting food
// worker of a better kind (OptFoodRank) takes the place of the worker whose kind reaches the
// least food. he needs his own building (his hire price with it, OptHireInfo, within your gold
// above the Market route), so the old worker's empty building is demolished first and its lot is
// free ($OptSwapOldBldg, OptSwapChore). the biggest step up wins; the same worker rules as the
// level swap (full HP, not in your party). $OptSwapType 1 for this kind of swap.
// sets $OptSwapOld / $OptSwapNew; returns 1 when there is one
function OptSwapPick()
{
   $OptSwapOld = 0;
   $OptSwapNew = 0;
   $OptSwapType = 0;
   if (!isObject(TownsPeople) || !isObject(gTown) || !isObject(MainCharacter)) { return 0; }
   %party = MainCharacter.getParty();
   if (!OptFoodShort() && OptFarmersShort())
   {
      %bestUp = 0;
      for (%i = 0; %i < $MaxNumberOfCandidates; %i++)
      {
         %c = gTown.AvailableCandidates[%i];
         if (!isObject(%c) || !isObject(%c.myCandidateData) || gTown.CandidateTimeRemaining[%i] < 0) { continue; }
         if (gTown.Fame < %c.FameRequired) { continue; }
         %rc = OptFoodRank(%c.myCandidateData.Profession);
         if (%rc <= 0 || !OptMakesFood(%c)) { continue; }
         // paid from all your gold: food comes before the Market route (as the food route's own
         // hires do); the Market reserve had kept a Farmer from replacing a Herder
         if (getField(strreplace(OptHireInfo(%c), "|", "\t"), 1) > gTown.Gold) { continue; }
         for (%j = 0; %j < TownsPeople.getCount(); %j++)
         {
            %w = TownsPeople.getObject(%j);
            if (!isObject(%w) || %w.getState() $= "Dead") { continue; }
            if (isObject(%party) && %party.getMemberIndex(%w) >= 0) { continue; }
            %rw = OptWorkerFoodRank(%w);
            if (%rw <= 0 || %rc - %rw <= %bestUp + 0.01) { continue; }
            if (%w.GetCurrentHealth() < %w.getMaxHealth()) { continue; }
            %bestUp = %rc - %rw;
            $OptSwapOld = %w;
            $OptSwapNew = %c;
         }
      }
      if (%bestUp > 0)
      {
         $OptSwapType = 1;
         return 1;
      }
   }
   %bestGain = 0;
   for (%i = 0; %i < $MaxNumberOfCandidates; %i++)
   {
      %c = gTown.AvailableCandidates[%i];
      if (!isObject(%c) || !isObject(%c.myCandidateData) || gTown.CandidateTimeRemaining[%i] < 0) { continue; }
      // paid from all your gold (the player's rule): the Market route's reserve had kept a L2
      // Farmer from replacing a L1 one with 17 gold in town and an 11 gold price
      if (gTown.Fame < %c.FameRequired || %c.HireCost > gTown.Gold) { continue; }
      %prof = %c.myCandidateData.Profession;
      %cl = %c.getApparentLevel();
      for (%j = 0; %j < TownsPeople.getCount(); %j++)
      {
         %w = TownsPeople.getObject(%j);
         if (!isObject(%w) || %w.getState() $= "Dead" || !isObject(%w.myCandidateData)) { continue; }
         if (%w.myCandidateData.Profession !$= %prof) { continue; }
         if (isObject(%party) && %party.getMemberIndex(%w) >= 0) { continue; }
         %b = %w.getHomeBuilding();
         if (!isObject(%b) || %b.getDataBlock().Occupant !$= %prof) { continue; }
         if (%w.GetCurrentHealth() < %w.getMaxHealth()) { continue; }
         %gain = %cl - %w.getApparentLevel();
         if (%gain > %bestGain)
         {
            if (%full[%prof] $= "") { %full[%prof] = OptJobFull(%prof); }
            if (!%full[%prof]) { continue; }
            %bestGain = %gain;
            $OptSwapOld = %w;
            $OptSwapNew = %c;
         }
      }
   }
   return %bestGain > 0;
}

// 1 when the plan wants nobody more of this profession: 6 Farmers (OptFarmersShort), and every
// line of the town plan for it has its worker or wants none now (not worth it, or waiting for a
// building lot that is not there: then only a swap can still raise a level). a job with no line
// (Herder, Trapper) is full
function OptJobFull(%prof)
{
   // food workers: no level swap before the 6 Farmers (a Trapper or Herder is traded for a
   // better kind before then, OptSwapPick)
   if (OptFoodRank(%prof) > 0) { return !OptFarmersShort(); }
   OptTownScan();
   for (%l = 0; %l < $OptLineN; %l++)
   {
      if (isObject($OptLW[%l]) || getWord($OptLine[%l], 1) !$= %prof) { continue; }
      %f = strreplace(OptLineAction(%l), "|", "\t");
      if (getField(%f, 0) $= "none") { continue; }
      if (getField(%f, 0) $= "wait" && strstr(getField(%f, 2), "when a building lot is free") >= 0) { continue; }
      return 0;
   }
   return 1;
}

// the townsperson a visiting food worker of a better kind would replace (OptSwapPick's trade-up)
// whether or not it can happen now (gold, the worker's HP): the weakest kind's worker, or 0
function OptTradeUpFor(%c)
{
   if (OptFoodShort() || !OptFarmersShort() || !isObject(%c.myCandidateData)) { return 0; }
   %rc = OptFoodRank(%c.myCandidateData.Profession);
   if (%rc <= 0 || !OptMakesFood(%c)) { return 0; }
   %party = MainCharacter.getParty();
   %best = 0;
   %bestUp = 0;
   for (%j = 0; %j < TownsPeople.getCount(); %j++)
   {
      %w = TownsPeople.getObject(%j);
      if (!isObject(%w) || %w.getState() $= "Dead") { continue; }
      if (isObject(%party) && %party.getMemberIndex(%w) >= 0) { continue; }
      %rw = OptWorkerFoodRank(%w);
      if (%rw <= 0 || %rc - %rw <= %bestUp + 0.01) { continue; }
      %bestUp = %rc - %rw;
      %best = %w;
   }
   return %best;
}

// the townsperson a visitor would replace in a level swap (OptSwapPick's rules) whether or not it
// can happen now (gold, fame, the worker's HP): the lowest level one of his profession, or 0
function OptLevelSwapFor(%c)
{
   if (!isObject(%c.myCandidateData)) { return 0; }
   %prof = %c.myCandidateData.Profession;
   if (!OptJobFull(%prof)) { return 0; }
   %party = MainCharacter.getParty();
   %cl = %c.getApparentLevel();
   %best = 0;
   %bestGain = 0;
   for (%j = 0; %j < TownsPeople.getCount(); %j++)
   {
      %w = TownsPeople.getObject(%j);
      if (!isObject(%w) || %w.getState() $= "Dead" || !isObject(%w.myCandidateData)) { continue; }
      if (%w.myCandidateData.Profession !$= %prof) { continue; }
      if (isObject(%party) && %party.getMemberIndex(%w) >= 0) { continue; }
      %b = %w.getHomeBuilding();
      if (!isObject(%b) || %b.getDataBlock().Occupant !$= %prof) { continue; }
      %gain = %cl - %w.getApparentLevel();
      if (%gain > %bestGain)
      {
         %bestGain = %gain;
         %best = %w;
      }
   }
   return %best;
}

// what a swap gives, for the step texts
function OptSwapWhy(%w, %c)
{
   if ($OptSwapType)
   {
      %b = %w.getHomeBuilding();
      %bn = isObject(%b) ? %b.getNameString() : "building";
      return "the " @ %c.getProfessionName() @ " visiting now replaces " @ %w.getNameString() @ " (" @ %bn @ "): a " @ %c.getProfessionName() @ "'s building reaches " @ OptNum(OptFoodRank(%c.myCandidateData.Profession) * $gDayApparentCycle) @ " food a day, the " @ %bn @ " line " @ OptNum(OptWorkerFoodRank(%w) * $gDayApparentCycle) @ " · the 6 food workers become Farmers one by one";
   }
   return "the " @ %c.getProfessionName() @ " L" @ %c.getApparentLevel() @ " visiting now takes " @ OptHis(%w) @ " place for " @ %c.HireCost @ " gold (" @ %w.getNameString() @ " is L" @ %w.getApparentLevel() @ ") · the same work, with more HP and defense against raiders";
}

// the swap's steps in town (OptSwapPick): what the worker wears comes off first (sending him away
// does not give it back, Creature::evictFromTown), then he is sent away, then the visitor is
// hired into his building. $OptSwapHire remembers the visitor while the worker is being sent
// away, so the hire is asked for even when no line of the plan wants one. returns 1 with a step
function OptSwapChore()
{
   %h = $OptSwapHire;
   // a food worker traded for a better kind left his building empty: it is demolished before the
   // hire, so its lot is free for the new worker's own building (Building::onFriendlySingleClicked:
   // clicking an empty building asks to demolish it)
   %ob = $OptSwapOldBldg;
   if (%ob !$= "" && !isObject(%ob)) { $OptSwapOldBldg = ""; }
   else if (%ob !$= "" && !isObject($OptSwapHireOld))
   {
      if (!isObject(%ob.getOccupant()))
      {
         OptNow("Demolish the empty " @ %ob.getNameString(), "click it and press Yes · frees its building lot" @ (isObject(%h) && %h.Role $= "Candidate" ? " · then hire the " @ %h.getProfessionName() : ""), %ob.getPosition());
         return 1;
      }
      $OptSwapOldBldg = "";
   }
   if (%h !$= "" && isObject(%h) && %h.Role $= "Candidate" && !isObject($OptSwapHireOld))
   {
      OptNow("Hire the " @ %h.getProfessionName() @ " (Visitors, top left)", "L" @ %h.getApparentLevel() @ ", " @ getField(strreplace(OptHireInfo(%h), "|", "\t"), 1) @ " gold · takes " @ $OptSwapHireOldName @ "'s place: " @ $OptSwapHireTxt, "");
      return 1;
   }
   $OptSwapHire = "";
   if (!OptSwapPick()) { return 0; }
   %w = $OptSwapOld;
   %c = $OptSwapNew;
   for (%k = 0; %k < 4; %k++)
   {
      %x = OptEquipped(%w, %k);
      if (%x $= "") { continue; }
      OptNow("Take " @ %x.getNameString() @ " off " @ %w.getNameString(), "click " @ %w.getNameString() @ ", drag it from " @ OptHis(%w) @ " " @ OptTPSlotName(%k) @ " slot into the town inventory or your bag: sending " @ OptHim(%w) @ " away doesn't give it back · then " @ OptHe(%w) @ " leaves town: " @ OptSwapWhy(%w, %c), %w.getPosition());
      return 1;
   }
   OptSwapStart(%w, %c);
   OptNow("Send " @ %w.getNameString() @ " away", "click " @ OptHim(%w) @ ", press " @ getStringTableEntry($ID_TOWNS_PERSON_LEAVE_TOWN) @ ", then Yes · " @ OptSwapWhy(%w, %c), %w.getPosition());
   return 1;
}

// the swap's memory while the worker is sent away (OptSwapChore): the visitor to hire, the worker,
// and for a food worker traded for a better kind his building, demolished before the hire
function OptSwapStart(%w, %c)
{
   $OptSwapHire = %c.getId();
   $OptSwapHireOld = %w.getId();
   $OptSwapHireOldName = %w.getNameString();
   $OptSwapHireTxt = $OptSwapType ? "more food, a better kind of food worker" : "the same work, with more HP and defense against raiders";
   %b = %w.getHomeBuilding();
   $OptSwapOldBldg = ($OptSwapType && isObject(%b)) ? %b.getId() : "";
}

// Hide for every townsperson while Guardian is open. sets the step, returns 1
function OptHideChore()
{
   if (!OptNeedGuardian()) { return 0; }
   for (%i = 0; %i < TownsPeople.getCount(); %i++)
   {
      %p = TownsPeople.getObject(%i);
      if (!isObject(%p) || %p.getState() $= "Dead") { continue; }
      if (!%p.getHideFromAttack())
      {
         OptNow("Set " @ %p.getNameString() @ " to hide from raiders", "click " @ OptHim(%p) @ " and press the button that says what " @ OptHe(%p) @ " does if the town is attacked, until it says \"" @ getStringTableEntry($ID_TOWNS_PERSON_DEFEND) @ "\" · Guardian: nobody may die", "");
         return 1;
      }
   }
   return 0;
}

// the player and town inventories in $OptInv[0..$OptInvN-1], names in $OptInvSrc
function OptInvList()
{
   $OptInvN = 0;
   %pi = MainCharacter.getParty().GetInventory();
   if (isObject(%pi)) { $OptInv[$OptInvN] = %pi; $OptInvSrc[$OptInvN] = "player inventory"; $OptInvN++; }
   %ti = gTown.GetInventory();
   if (isObject(%ti)) { $OptInv[$OptInvN] = %ti; $OptInvSrc[$OptInvN] = "town inventory"; $OptInvN++; }
}

// the work bonus an item gives a worker of this profession: Creature::applyItemBonuses adds
// ProductionBonusAmount when the item's ProductionProfessions names the profession
function OptWorkBonus(%idb, %prof)
{
   if (%prof $= "" || %idb.ProductionBonusAmount <= 0) { return 0; }
   if (strstr(" " @ %idb.ProductionProfessions @ " ", " " @ %prof @ " ") < 0) { return 0; }
   return %idb.ProductionBonusAmount;
}

// the four equipment slots as the townsperson window labels them
function OptTPSlotName(%s)
{
   if (%s == 0) { return getStringTableEntry($ID_INVENTORY_SLOT_WEAPON); }
   if (%s == 1) { return getStringTableEntry($ID_INVENTORY_SLOT_ARMOR); }
   if (%s == 2) { return getStringTableEntry($ID_INVENTORY_SLOT_OFF_HAND); }
   return getStringTableEntry($ID_INVENTORY_SLOT_ARTIFACT);
}

// while David is open, the attack of the best bow you own (it is never given away or sold), else ""
function OptKeepBowAttack()
{
   if (OptHave($Achievement_David) || MainCharacter.DifficultyLevel < 2) { return ""; }
   %b = OptBestBow();
   return %b $= "" ? "" : getWord(%b, 0);
}

// an item with an on-hit or when-hit effect (fire, ice, lightning, stun) the stat check can't see
function OptHasEffect(%it)
{
   %idb = %it.getDataBlock();
   if (%idb.hitProc !$= "" || %idb.getHitProc !$= "" || %idb.friendlyProc !$= "" || %idb.enemyProc !$= "" || %idb.endAttackProc !$= "") { return 1; }
   // a prefix or suffix with an effect of its own (Brutal, Flaming, of Lightning, ...)
   %pm = %it.PrefixModifier;
   %sm = %it.SuffixModifier;
   if (isObject(%pm) && (%pm.hitProc !$= "" || %pm.getHitProc !$= "")) { return 1; }
   return isObject(%sm) && (%sm.hitProc !$= "" || %sm.getHitProc !$= "");
}

// the item a townsperson should wear, from the player or town inventory: first one that speeds up
// his work (it names his profession), then armor, a shield or an artifact for an empty slot (more
// defense and HP if raiders reach him). anything better for you is equipped by the gear step
// before this runs. sets $OptGiveWho/Item/Slot/Src/Why, returns 1 when there is one
function OptGivePick()
{
   $OptGiveWho = 0;
   if (!isObject(TownsPeople) || TownsPeople.getCount() < 1) { return 0; }
   %bowA = OptKeepBowAttack();
   OptInvList();
   %bestV = 0;
   for (%v = 0; %v < $OptInvN; %v++)
   {
      %inv = $OptInv[%v];
      %slots = %inv.GetNumSlots();
      for (%k = 0; %k < %slots; %k++)
      {
         %it = %inv.GetItem(%k);
         if (!isObject(%it)) { continue; }
         %idb = %it.getDataBlock();
         if (%idb.Useable) { continue; }
         if (%bowA !$= "" && %idb.ActionType $= "missile attack" && %it.getAttack() >= %bowA) { continue; }
         %armor = %idb.category !$= "Weapon" && (%it.getDefense() > 0 || %it.getHealth() > 0);
         for (%i = 0; %i < TownsPeople.getCount(); %i++)
         {
            %p = TownsPeople.getObject(%i);
            if (!isObject(%p) || %p.getState() $= "Dead") { continue; }
            // the worker being swapped for a higher-level visitor gets nothing (OptSwapPick)
            if (isObject($OptSwapOld) && %p.getId() == $OptSwapOld.getId()) { continue; }
            %s = OptItemSlot(%p, %idb);
            if (%s $= "" || OptHandsBlocked(%p, %idb, %s)) { continue; }
            %cur = OptEquipped(%p, %s);
            %prof = %p.myCandidateData.Profession;
            %bn = OptWorkBonus(%idb, %prof);
            %bc = isObject(%cur) ? OptWorkBonus(%cur.getDataBlock(), %prof) : 0;
            if (%bn > %bc)
            {
               %val = 1000 + (%bn - %bc) * 1000;
               %why = OptHe(%p) @ " works faster (+" @ OptNum(%bn) @ " bonus for a " @ %p.getProfessionName() @ ")";
            }
            else if (%armor && !isObject(%cur))
            {
               %val = %it.getDefense() + %it.getHealth() / 4;
               %why = "+" @ %it.getDefense() @ " defense, +" @ %it.getHealth() @ " HP if raiders reach " @ OptHim(%p);
            }
            else { continue; }
            if (%val > %bestV)
            {
               %bestV = %val;
               $OptGiveWho = %p;
               $OptGiveItem = %it.getNameString();
               $OptGiveSlot = OptTPSlotName(%s);
               $OptGiveSrc = $OptInvSrc[%v];
               $OptGiveWhy = %why;
            }
         }
      }
   }
   return isObject($OptGiveWho);
}

// the worn item's name when it beats %it in every stat (attack, defense, HP, critical, swing
// speed, and the same kind of weapon), else ""
function OptBetterWorn(%c, %s, %it)
{
   %cur = OptEquipped(%c, %s);
   if (!isObject(%cur)) { return ""; }
   %idb = %it.getDataBlock();
   %udb = %cur.getDataBlock();
   if (%idb.category $= "Weapon" && %udb.ActionType !$= %idb.ActionType) { return ""; }
   %cdb = %c.getDataBlock();
   %s1 = %it.getSpeed() + OptWeaponBonus(%cdb, %idb);
   %s0 = %cur.getSpeed() + OptWeaponBonus(%cdb, %udb);
   if (%it.getAttack() > %cur.getAttack() || %it.getDefense() > %cur.getDefense() || %it.getHealth() > %cur.getHealth() || %it.getCriticalBonus() > %cur.getCriticalBonus() || %s1 < %s0) { return ""; }
   return %cur.getNameString();
}

// gear to sell. needs a Market with its Merchant: then dragging an item out of the inventory and
// pressing Yes sells it (without one the same drag destroys it). an item qualifies when everyone in
// your party who can wear it already wears something at least as good in every stat, no townsperson
// works faster with it, it has no special effect, and it is not the bow kept for David.
// sets $OptSellItem/Src/Why, returns 1 when there is one
function OptSellPick()
{
   $OptSellItem = "";
   if (!gTown.HasOccupiedBuilding(dbBuildingMarket) && !gTown.HasOccupiedBuilding(dbBuildingOrcMarket)) { return 0; }
   return OptJunkPick(0);
}

// FREE SLOTS: ALWAYS 1 IN YOUR BAG AND 1 IN THE TOWN INVENTORY (the player's rule, after
// "whenever all inventories are full, we remove the worst item we have": his bag and the town
// inventory were both full with no Merchant yet, so "Take Mystic Robes off Roesia" had nowhere to
// put them; then "let's make sure we always have 4 items free, 4 in town, 4 in personal
// inventory"; then "until we have the merchant, no need to always keep 4 free slots. we can keep
// 1"; then, asked how many: "1 always", with or without a Merchant: $Opt::FreeSlots, OptKeepFree). an
// inventory with fewer free slots than that loses its worst item, one
// at a time: junk first (OptJunkAll: it gives you nothing over your gear and nobody in the plan
// works with it), the one worth the least gold (Value x the stack); with no junk in it, the item
// worth the least that is not a potion or other usable item, the bow kept for David, the king's
// requested item or a work or building item the plan uses. the town inventory only while you
// are in town (it is only used there). dragging it out of the inventory and pressing Yes removes
// it (ShowConfirmItemRemoveDialog; with a Merchant in town it is sold). its slot flashes red
// (OptJunkMarks). returns 1 with the step
function OptFreeSlotsTown()
{
   %ti = gTown.GetInventory();
   if (!isObject(%ti)) { return 99; }
   %n = 0;
   for (%k = 0; %k < %ti.GetNumSlots(); %k++) { if (!isObject(%ti.GetItem(%k))) { %n++; } }
   return %n;
}

// free slots kept in each inventory: $Opt::FreeSlots (1), Merchant or not
function OptKeepFree()
{
   return $Opt::FreeSlots;
}

function OptFullInvStep()
{
   %inTown = OptLordInTown();
   %pi = MainCharacter.getParty().GetInventory();
   %ti = gTown.GetInventory();
   %freeP = OptFreeSlots();
   %freeT = OptFreeSlotsTown();
   %keep = OptKeepFree();
   %shortP = isObject(%pi) && %freeP < %keep;
   // the town inventory only while a townsperson makes items: they are what fill it (the player:
   // with no townsfolk the guide asked to move an item out of it, "that makes no sense, because
   // there's no townsfolk creating items anyway"). OptTownRoom names a maker, or ""
   %shortT = %inTown && isObject(%ti) && %freeT < %keep && OptTownRoom() !$= "";
   if (!%shortP && !%shortT) { return 0; }
   %bInv = 0;
   %bSlot = -1;
   %bVal = "";
   OptJunkAll(%shortT);
   for (%n = 0; %n < $OptJunkN; %n++)
   {
      if (!isObject($OptJunkId[%n])) { continue; }
      if ($OptJunkTown[%n] ? !%shortT : !%shortP) { continue; }
      %amt = $OptJunkInv[%n].GetItemAmount($OptJunkSlot[%n]);
      if (%amt < 1) { %amt = 1; }
      %val = ($OptJunkId[%n].getDataBlock().Value + 0) * %amt;
      if (%bSlot >= 0 && %val >= %bVal) { continue; }
      %bInv = $OptJunkInv[%n];
      %bSlot = $OptJunkSlot[%n];
      %bVal = %val;
   }
   %why = "it gives you nothing over your gear";
   if (%bSlot < 0)
   {
      %bowA = OptKeepBowAttack();
      %plan = OptPlanProfessions();
      %lord = $OptObj["P", $OptLord];
      OptInvList();
      for (%v = 0; %v < $OptInvN; %v++)
      {
         if (($OptInvSrc[%v] $= "town inventory") ? !%shortT : !%shortP) { continue; }
         %inv = $OptInv[%v];
         for (%k = 0; %k < %inv.GetNumSlots(); %k++)
         {
            %it = %inv.GetItem(%k);
            if (!isObject(%it)) { continue; }
            %idb = %it.getDataBlock();
            if (%idb.Useable) { continue; }
            if (%bowA !$= "" && %idb.ActionType $= "missile attack" && %it.getAttack() >= %bowA) { continue; }
            if (isObject(RequestFromKing) && isObject(RequestFromKing.requestedItem) && RequestFromKing.requestedItem.getDataBlock().getId() == %idb.getId()) { continue; }
            if (isObject(%lord) && OptItemSlot(%lord, %idb) $= "" && !OptWorkItemUnused(%idb, %plan)) { continue; }
            %amt = %inv.GetItemAmount(%k);
            if (%amt < 1) { %amt = 1; }
            %val = (%idb.Value + 0) * %amt;
            if (%bSlot >= 0 && %val >= %bVal) { continue; }
            %bInv = %inv;
            %bSlot = %k;
            %bVal = %val;
         }
      }
      %why = "nothing there is junk, so the item worth the least gold";
   }
   if (%bSlot < 0 || !isObject(%bInv)) { return 0; }
   %it = %bInv.GetItem(%bSlot);
   if (!isObject(%it)) { return 0; }
   %isTown = isObject(%ti) && %bInv.getId() == %ti.getId();
   %free = %isTown ? %freeT : %freeP;
   // its slot flashes red, as in "Sell your junk"
   $OptJunkN = 1;
   $OptJunkCount = 1;
   $OptJunkInv[0] = %bInv;
   $OptJunkTown[0] = %isTown;
   $OptJunkSlot[0] = %bSlot;
   $OptJunkId[0] = %it.getId();
   $OptJunkStep = 1;
   %sell = gTown.HasOccupiedBuilding(dbBuildingMarket) || gTown.HasOccupiedBuilding(dbBuildingOrcMarket);
   // THE TOWN INVENTORY FULL AND YOUR BAG NOT (the player's rule: "stop telling me to remove items
   // from town inventory when it is full, if my personal inventory isn't. instead, tell me - move
   // X item to personal inventory"): the same item moves to your bag
   if (%isTown && %freeP > 0)
   {
      OptNow("Move " @ %it.getNameString() @ " to your personal inventory", "the slot flashing red: drag it from the town inventory into your personal inventory · the town inventory has " @ %free @ " free slot" @ (%free == 1 ? "" : "s") @ ", " @ %keep @ (%keep == 1 ? " is" : " are") @ " kept free · your personal inventory has " @ %freeP @ " · the worst item there: " @ %why @ ", worth " @ %bVal @ " gold", "");
      return 1;
   }
   OptNow("Remove " @ %it.getNameString(), "the slot flashing red: drag it out of the " @ (%isTown ? "town inventory" : "player inventory") @ " and press Yes" @ (%sell ? ", the Merchant buys it" : " (no Merchant yet, so it is just gone)") @ " · " @ (%isTown ? "the town inventory" : "your bag") @ " has " @ %free @ " free slot" @ (%free == 1 ? "" : "s") @ ", " @ %keep @ (%keep == 1 ? " is" : " are") @ " kept free" @ " · the worst item there: " @ %why @ ", worth " @ %bVal @ " gold", "");
   return 1;
}

// the town inventory has 18 slots; when it is full, an item a maker finishes is lost
// (TownBuilding daily update: "Failed to add item to town inventory"). "free maker" or ""
// when nobody is making items
function OptTownRoom()
{
   %inv = gTown.GetInventory();
   if (!isObject(%inv)) { return ""; }
   %free = 0;
   for (%k = 0; %k < %inv.GetNumSlots(); %k++) { if (!isObject(%inv.GetItem(%k))) { %free++; } }
   %maker = "";
   for (%i = 0; %i < TownsPeople.getCount() && %maker $= ""; %i++)
   {
      %p = TownsPeople.getObject(%i);
      if (!isObject(%p) || %p.getState() $= "Dead") { continue; }
      %b = %p.getHomeBuilding();
      if (isObject(%b) && %b.occupantActionType $= "Producing") { %maker = %p.getNameString(); }
   }
   if (%maker $= "") { return ""; }
   return %free SPC %maker;
}

// the town inventory is full while someone makes items: make room. junk (OptJunkPick) moves to
// your bag while it has room to spare for loot (it can still be sold once there is a Market),
// otherwise it is removed (without a Market that destroys it, which only loses a later sale).
// sets the step, returns 1
function OptTownRoomStep()
{
   %r = OptTownRoom();
   if (%r $= "" || getWord(%r, 0) > 0) { return 0; }
   %maker = getWords(%r, 1);
   %bag = OptFreeSlots();
   if (!OptJunkPick(1))
   {
      if (%bag > 2) { OptNow("Move an item from the town inventory to your bag", "the town inventory is full (18 of 18): " @ %maker @ "'s next item would be lost · nothing there is junk, so move the one you need least", ""); return 1; }
      return 0;
   }
   if (%bag > 2)
   {
      OptNow("Move " @ $OptSellItem @ " to your bag", "drag it from the town inventory into your player inventory · the town inventory is full (18 of 18): " @ %maker @ "'s next item would be lost · " @ $OptSellWhy @ ", sell it once you have a Market", "");
   }
   else
   {
      OptNow("Remove " @ $OptSellItem, "drag it out of the town inventory and press Yes (no Market yet, so it is just gone) · the town inventory is full (18 of 18): " @ %maker @ "'s next item would be lost · " @ $OptSellWhy, "");
   }
   return 1;
}

// ALL the junk you can sell now, for one "sell your junk" step. an item is junk when the fight
// math (the gear scan, against the site you are sent to) says wearing it instead of your gear
// gives you nothing; when that number is missing (a site you can't survive, a two-handed weapon
// next to a shield), only when your worn item beats it in every stat. never junk: potions and
// other usable items, the bow kept for David, items a townsperson works faster with, and armor a
// townsperson has an empty slot for. an item only a townsperson or a building can use (a work
// tool, a building's cost item) is junk when nobody in the plan uses it (OptWorkItemUnused, the
// player's rule); one somebody in the plan uses stays.
// %inTown: the town inventory too. sets $OptJunkN (stacks), $OptJunkCount (items), $OptJunkText
function OptJunkAll(%inTown)
{
   $OptJunkN = 0;
   $OptJunkCount = 0;
   $OptJunkText = "";
   %L = $OptLord;
   %lord = $OptObj["P", %L];
   if (!isObject(%lord)) { return; }
   %bowA = OptKeepBowAttack();
   %fresh = $OptGearReady && $OptGearBase < 9999;
   %wgFresh = OptWGFresh();
   %plan = OptPlanProfessions();
   OptInvList();
   for (%v = 0; %v < $OptInvN; %v++)
   {
      %town = $OptInvSrc[%v] $= "town inventory";
      if (%town && !%inTown) { continue; }
      %inv = $OptInv[%v];
      %slots = %inv.GetNumSlots();
      for (%k = 0; %k < %slots; %k++)
      {
         %it = %inv.GetItem(%k);
         if (!isObject(%it)) { continue; }
         %idb = %it.getDataBlock();
         if (%idb.Useable) { continue; }
         // the item's id, for its red box (OptJunkMarks / OptJunkBlink). set here, before the gear
         // part below: a work item skips that part, and with the id set inside it a Trap, Snare,
         // Dragon Egg or Golden Lyre was listed but its box stayed dark (id never matched)
         %id = %it.getId();
         %s = OptItemSlot(%lord, %idb);
         // not for your lord: sold only when nobody in the plan uses it
         if (%s $= "" && !OptWorkItemUnused(%idb, %plan)) { continue; }
         if (%s !$= "") {
         if (%bowA !$= "" && %idb.ActionType $= "missile attack" && %it.getAttack() >= %bowA) { continue; }
         // a townsperson would work faster with it than with what that slot holds now, or has an
         // empty slot for this armor (the give step hands those out)
         %keep = 0;
         %armor = %idb.category !$= "Weapon" && (%it.getDefense() > 0 || %it.getHealth() > 0);
         for (%i = 0; %i < TownsPeople.getCount() && !%keep; %i++)
         {
            %p = TownsPeople.getObject(%i);
            if (!isObject(%p) || %p.getState() $= "Dead") { continue; }
            %ps = OptItemSlot(%p, %idb);
            if (%ps $= "" || OptHandsBlocked(%p, %idb, %ps)) { continue; }
            %pc = OptEquipped(%p, %ps);
            %prof = %p.myCandidateData.Profession;
            %bn = OptWorkBonus(%idb, %prof);
            if (%bn > 0 && %bn > (isObject(%pc) ? OptWorkBonus(%pc.getDataBlock(), %prof) : 0)) { %keep = 1; }
            else if (%armor && !isObject(%pc)) { %keep = 1; }
         }
         if (%keep) { continue; }
         // a healing wand or staff can't fight: nothing for you (Lone Wolf, no one to heal)
         %attacks = %idb.category !$= "Weapon" || %idb.ActionType $= "melee attack" || %idb.ActionType $= "missile attack";
         if (%attacks)
         {
            // the keep check: junk only if it beats your worn gear in no fight still ahead.
            // while it is still being checked, only an item your gear beats in every stat
            %verdict = OptKeepVerdict(%it);
            if (getWord(%verdict, 0) $= "keep") { continue; }
            // never an item the every-enemy totals say you should wear, and nothing goes on the
            // keep check's word alone without its total: the item's own once done for what you
            // wear now (OptWGItemSum, item by item), else the last finished set's
            if (%verdict $= "junk")
            {
               %ws = OptWGItemSum(%id);
               if (%ws $= "" && %wgFresh && $OptWGPubTagOf[%id] == $OptWGPubTag) { %ws = $OptWGPubSumOf[%id]; }
               if (%ws $= "" || %ws <= -0.5) { continue; }
            }
            if (%verdict $= "" && (OptHasEffect(%it) || OptBetterWorn(%lord, %s, %it) $= "")) { continue; }
         }
         }
         %amt = %inv.GetItemAmount(%k);
         if (%amt < 1) { %amt = 1; }
         // where it lies, for the flashing marks on the inventory slots (OptJunkMarks)
         $OptJunkInv[$OptJunkN] = %inv;
         $OptJunkTown[$OptJunkN] = %town;
         $OptJunkSlot[$OptJunkN] = %k;
         $OptJunkId[$OptJunkN] = %id;
         $OptJunkN++;
         $OptJunkCount += %amt;
         if ($OptJunkN <= 8) { $OptJunkText = $OptJunkText @ ($OptJunkN > 1 ? ", " : "") @ %it.getNameString() @ (%amt > 1 ? " x" @ %amt : "") @ (%town ? " (town)" : ""); }
      }
   }
   if ($OptJunkN > 8) { $OptJunkText = $OptJunkText @ " and " @ ($OptJunkN - 8) @ " more"; }
}

// the professions the plan uses, as " word word ... ": everyone living in town now, the worker of
// every building on the town plan's lines (every stage: Goodman, Innkeeper, Brewer, Merchant,
// Tinker, Smith, Herbalist, Doctor, Alchemist, Wizard, Guard, Fortune Teller...), and the Farmer
// the food route hires.
// THE INFIRMARY'S DOCTOR ONLY WHEN IT IS WANTED (the player's rule: the Basic and Advanced
// Medical Treatises, Doctor-only work items, were kept though no Doctor was coming): the potions
// line lists the Infirmary, but it is built only for another line's town quality
// (OptInfirmaryFor). its Doctor counts while that is so or an Infirmary stands; a Doctor living in
// town counts anyway (the townsfolk above)
function OptPlanProfessions()
{
   %inf = OptTownHasBuilding(dbBuildingInfirmary);
   if (!%inf)
   {
      OptTownScan();
      %inf = OptInfirmaryFor() !$= "";
   }
   %t = " Farmer ";
   for (%i = 0; %i < TownsPeople.getCount(); %i++)
   {
      %p = TownsPeople.getObject(%i);
      if (!isObject(%p) || %p.getState() $= "Dead" || !isObject(%p.myCandidateData)) { continue; }
      %t = %t @ %p.myCandidateData.Profession @ " ";
   }
   for (%l = 0; %l < $OptLineN; %l++)
   {
      %t = %t @ getWord($OptLine[%l], 1) @ " ";
      for (%k = 2; %k < getWordCount($OptLine[%l]); %k++)
      {
         %b = getWord($OptLine[%l], %k);
         if (!%inf && %b $= "dbBuildingInfirmary") { continue; }
         if (isObject(%b) && %b.Occupant !$= "") { %t = %t @ %b.Occupant @ " "; }
      }
   }
   return %t;
}

// 1 when a town building of this kind stands in town
function OptTownHasBuilding(%bdb)
{
   if (!isObject(TownBuildingsGroup) || !isObject(%bdb)) { return 0; }
   %id = %bdb.getId();
   for (%i = 0; %i < TownBuildingsGroup.getCount(); %i++)
   {
      %b = TownBuildingsGroup.getObject(%i);
      if (isObject(%b) && %b.getDataBlock().getId() == %id) { return 1; }
   }
   return 0;
}

// 1 for an item only a townsperson or a building can use that nobody in the plan uses: a work
// tool (ProductionProfessions) whose professions are all outside %plan (OptPlanProfessions) or
// that gives no output or town quality bonus, or a
// building's cost item (the game's item sets: tome for the Tower, crystal ball for the Tent, lute
// for the Stage, dragon egg for the Dragon Ranch, relic for the Residence, remains for the
// Graveyard) that no building still to be built on the town plan's lines needs. never an item the
// king's request asks for. 0 for anything else (potions, bloods, quest items stay)
function OptWorkItemUnused(%idb, %plan)
{
   %profs = %idb.ProductionProfessions;
   %name = %idb.getName();
   %need = "";
   if (isFunction("GetAllItemsFromSet"))
   {
      %sets = "Set_Tome Set_Orb Set_Instruments Set_Dragon_Eggs Set_Artifact Set_Remains";
      for (%w = 0; %w < getWordCount(%sets) && %need $= ""; %w++)
      {
         if (strstr(" " @ GetAllItemsFromSet(getWord(%sets, %w)) @ " ", " " @ %name @ " ") >= 0) { %need = getWord(%sets, %w); }
      }
   }
   if (%profs $= "" && %need $= "") { return 0; }
   if (isObject(RequestFromKing) && isObject(RequestFromKing.requestedItem) && RequestFromKing.requestedItem.getDataBlock().getId() == %idb.getId()) { return 0; }
   // A WORK ITEM ONLY COUNTS WHEN IT GIVES ITS WORKER SOMETHING: more output (ProductionBonusAmount,
   // added to what the building makes) or town quality (TownQualityBonus), as
   // Creature::applyItemDependentBonuses applies them. the player asked why Candles, Lamp and the
   // Magnifying Glass were never sold (they give nothing at all), then "fuck salve and bandages"
   // (their only gift, +0.2 town healing, does not count)
   %gives = %idb.ProductionBonusAmount > 0 || %idb.TownQualityBonus != 0;
   for (%w = 0; %gives && %w < getWordCount(%profs); %w++)
   {
      if (strstr(%plan, " " @ getWord(%profs, %w) @ " ") >= 0) { return 0; }
   }
   if (%need !$= "")
   {
      for (%l = 0; %l < $OptLineN; %l++)
      {
         for (%k = 2; %k < getWordCount($OptLine[%l]); %k++)
         {
            %b = getWord($OptLine[%l], %k);
            if (!isObject(%b) || (%b.Cost_Item_1 !$= %need && %b.Cost_Item_2 !$= %need)) { continue; }
            if (!OptTownHasBuilding(%b)) { return 0; }
         }
      }
   }
   return 1;
}

// FLASHING MARKS on the junk items' inventory slots while the step is "Sell your junk". the
// game's slots are the controls PartyInventoryIcon1-18 and TownInventoryIcon1-18 (setupInventory
// in game.cs / town.cs; slot 0 is icon 1). a mark is a red box laid over its slot that takes no
// mouse input (profile modal 0, like the game's own amount text), so the item under it is still
// dragged as usual
function OptJunkMarkCtrl(%town, %k)
{
   %name = "OptJunkMark" @ (%town ? "T" : "P") @ %k;
   %icon = (%town ? "TownInventoryIcon" : "PartyInventoryIcon") @ (%k + 1);
   if (!isObject(%icon)) { return 0; }
   // a box made earlier stays only while it sits on THIS slot's icon: one left on an icon the
   // game has since replaced (a game loaded, the screen rebuilt) is never seen. it is made again
   if (isObject(%name))
   {
      %g = %name.getGroup();
      if (isObject(%g) && %g.getId() == %icon.getId()) { return %name.getId(); }
      %name.delete();
   }
   if (!isObject(OptJunkProfile))
   {
      new GuiControlProfile(OptJunkProfile)
      {
         opaque = 1;
         fillColor = "255 40 40 80";
         border = 1;
         borderThickness = 2;
         borderColor = "255 80 80 255";
         modal = 0;
      };
   }
   %m = new GuiControl(%name)
   {
      Profile = "OptJunkProfile";
      HorizSizing = "width";
      VertSizing = "height";
      position = "0 0";
      Extent = %icon.getExtent();
      Visible = 0;
   };
   %icon.add(%m);
   return %m;
}

// marks the slots of the junk list (%on 1, from the plan that just chose the "Sell your junk"
// step) or clears every mark (%on 0)
function OptJunkMarks(%on)
{
   for (%n = 0; %n < $OptMarkN; %n++)
   {
      if (isObject($OptMark[%n])) { $OptMark[%n].setVisible(0); }
   }
   $OptMarkN = 0;
   if (!%on) { return; }
   for (%n = 0; %n < $OptJunkN; %n++)
   {
      %m = OptJunkMarkCtrl($OptJunkTown[%n], $OptJunkSlot[%n]);
      if (!isObject(%m)) { continue; }
      %ext = %m.getGroup().getExtent();
      %m.resize(0, 0, getWord(%ext, 0), getWord(%ext, 1));
      $OptMark[$OptMarkN] = %m;
      $OptMarkInv[$OptMarkN] = $OptJunkInv[%n];
      $OptMarkSlot[$OptMarkN] = $OptJunkSlot[%n];
      $OptMarkId[$OptMarkN] = $OptJunkId[%n];
      $OptMarkN++;
   }
   OptJunkBlink();
   // once per junk list: where each item's red box is, so one that doesn't show can be read back
   %key = "";
   for (%n = 0; %n < $OptJunkN; %n++) { %key = %key @ $OptJunkId[%n] @ "_" @ $OptJunkTown[%n] @ $OptJunkSlot[%n] @ " "; }
   if (%key !$= $OptJunkMarkLogged)
   {
      $OptJunkMarkLogged = %key;
      for (%n = 0; %n < $OptJunkN; %n++)
      {
         %icon = ($OptJunkTown[%n] ? "TownInventoryIcon" : "PartyInventoryIcon") @ ($OptJunkSlot[%n] + 1);
         %it = isObject($OptJunkId[%n]) ? $OptJunkId[%n].getNameString() : "?";
         %mk = "OptJunkMark" @ ($OptJunkTown[%n] ? "T" : "P") @ $OptJunkSlot[%n];
         %on = isObject(%mk) && isObject(%mk.getGroup()) ? %mk.getGroup().getName() : "none";
         %vis = isObject(%icon) ? (%icon.isVisible() @ "/" @ (isObject(%icon.getGroup()) ? %icon.getGroup().isVisible() : "?")) : "no icon";
         %now = isObject($OptJunkInv[%n]) && isObject($OptJunkInv[%n].GetItem($OptJunkSlot[%n])) ? $OptJunkInv[%n].GetItem($OptJunkSlot[%n]).getNameString() : "empty";
         echo("Opt junk mark: " @ %it @ " in the " @ ($OptJunkTown[%n] ? "town" : "bag") @ " slot " @ ($OptJunkSlot[%n] + 1) @ " | icon " @ %icon @ " shown " @ %vis @ " | box on " @ %on @ " | slot holds " @ %now);
      }
   }
}

// the flash (about twice a second). a mark goes out the moment its item has left that slot, so a
// sold item never leaves a red box on an empty slot
function OptJunkBlink()
{
   if ($OptMarkN < 1) { return; }
   %lit = mFloor(getRealTime() / 350) % 2;
   for (%n = 0; %n < $OptMarkN; %n++)
   {
      %m = $OptMark[%n];
      if (!isObject(%m)) { continue; }
      %inv = $OptMarkInv[%n];
      %it = isObject(%inv) ? %inv.GetItem($OptMarkSlot[%n]) : 0;
      if (!isObject(%it) || %it.getId() != $OptMarkId[%n]) { %m.setVisible(0); continue; }
      %m.setVisible(%lit);
   }
}

// gear nobody needs: everyone in your party who can wear it already wears something at least as
// good in every stat, no townsperson works faster with it, it has no special effect, and it is
// not the bow kept for David. %townOnly 1: the town inventory only, cheapest item (Value) first.
// sets $OptSellItem/Src/Why, returns 1 when there is one
function OptJunkPick(%townOnly)
{
   $OptSellItem = "";
   %bowA = OptKeepBowAttack();
   OptInvList();
   %bestV = "";
   for (%v = 0; %v < $OptInvN; %v++)
   {
      if (%townOnly && $OptInvSrc[%v] !$= "town inventory") { continue; }
      %inv = $OptInv[%v];
      %slots = %inv.GetNumSlots();
      for (%k = 0; %k < %slots; %k++)
      {
         %it = %inv.GetItem(%k);
         if (!isObject(%it)) { continue; }
         %idb = %it.getDataBlock();
         if (%idb.Useable) { continue; }
         if (%idb.category $= "Weapon" && %idb.ActionType !$= "melee attack" && %idb.ActionType !$= "missile attack") { continue; }
         if (%bowA !$= "" && %idb.ActionType $= "missile attack" && %it.getAttack() >= %bowA) { continue; }
         if (OptHasEffect(%it)) { continue; }
         %work = 0;
         for (%i = 0; %i < TownsPeople.getCount() && !%work; %i++)
         {
            %p = TownsPeople.getObject(%i);
            if (isObject(%p) && %p.getState() !$= "Dead" && OptWorkBonus(%idb, %p.myCandidateData.Profession) > 0) { %work = 1; }
         }
         if (%work) { continue; }
         %fits = 0;
         %ok = 1;
         %why = "";
         for (%m = 0; %m < $OptN["P"] && %ok; %m++)
         {
            %c = $OptObj["P", %m];
            if (!isObject(%c)) { continue; }
            %s = OptItemSlot(%c, %idb);
            if (%s $= "") { continue; }
            %fits = 1;
            %w = OptBetterWorn(%c, %s, %it);
            if (%w $= "") { %ok = 0; }
            else if (%why $= "") { %why = "your " @ %w @ " is as good or better in every stat"; }
         }
         if (!%fits || !%ok) { continue; }
         if (%townOnly && %bestV !$= "" && %idb.Value >= %bestV) { continue; }
         $OptSellItem = %it.getNameString();
         $OptSellSrc = $OptInvSrc[%v];
         $OptSellWhy = %why;
         if (!%townOnly) { return 1; }
         %bestV = %idb.Value + 0;
      }
   }
   return $OptSellItem !$= "";
}

// the hireable visitor who adds the most food per day, or 0
function OptBestFoodHire()
{
   %best = 0;
   %bestNet = 0;
   for (%i = 0; %i < $MaxNumberOfCandidates; %i++)
   {
      %c = gTown.AvailableCandidates[%i];
      if (!isObject(%c)) { continue; }
      %info = strreplace(OptHireInfo(%c), "|", "\t");
      %net = getField(%info, 3);
      if (%net > %bestNet && getField(%info, 1) <= gTown.Gold && gTown.Fame >= %c.FameRequired) { %best = %c; %bestNet = %net; }
   }
   return %best;
}

function OptHireText()
{
   %out = "";
   // with Lone Wolf done, fighters may join your party: show what each would do in your next fight
   %party = !OptNeedLoneWolf() && $OptSiteN > 0;
   if (%party)
   {
      OptLoadParty();
      OptLoadSite($OptSite[$OptSiteIdx[0]]);
      %base = getWord(OptFight(), 0);
      if (%base $= "inf" || $OptN["P"] >= $MaxPartySize) { %party = 0; }
   }
   for (%i = 0; %i < $MaxNumberOfCandidates; %i++)
   {
      %c = gTown.AvailableCandidates[%i];
      if (!isObject(%c)) { continue; }
      %info = strreplace(OptHireInfo(%c), "|", "\t");
      %line = getField(%info, 2);
      if (%party)
      {
         %k = $OptN["P"];
         OptSetUnit("P", %k, %c);
         $OptN["P"]++;
         %d = getWord(OptFight(), 0) - %base;
         $OptN["P"]--;
         %line = %line @ ", in your party: " @ OptNum(%d) @ " damage taken next fight";
      }
      %out = %out @ "  " @ %line @ "\n";
   }
   if (%out $= "") { return ""; }
   if (OptNeedGuardian()) { %hdr = "  hire what the town plan asks for; each one's window must say \"" @ getStringTableEntry($ID_TOWNS_PERSON_DEFEND) @ "\" (Guardian)"; }
   else { %hdr = "  hiring is safe" @ (OptNeedLoneWolf() ? ", but no one joins your party (Lone Wolf)" : ""); }
   return $Opt::Gold @ "VISITORS" @ $Opt::Grey @ %hdr @ $Opt::White @ "\n" @ %out;
}

// ------------------------------------------------------------------ town state

function OptFoodNet()
{
   %food = 0;
   for (%i = 0; %i < TownBuildingsGroup.getCount(); %i++) { %food += TownBuildingsGroup.getObject(%i).calcFoodperDay(0); }
   %food += gTown.FoodBonusPerDay;
   %eat = 0;
   %party = MainCharacter.getParty();
   for (%i = 0; %i < %party.getPartySize(); %i++) { %eat += %party.getMember(%i).foodConsumption; }
   for (%i = 0; %i < TownsPeople.getCount(); %i++) { %eat += TownsPeople.getObject(%i).foodConsumption; }
   // buildings and FoodBonusPerDay pay out every game day; everyone eats once per shown day
   // ($gDayApparentCycle game days), as in PlayerTown::GetRemainingDaysFood
   return %food * $gDayApparentCycle - %eat;
}

function OptLordInTown()
{
   return gTown.Contains(MainCharacter.getPosition());
}

// NO POTION IN TOWN (the player: "from the moment I enter town, drink a potion should NEVER
// appear"; it showed at the town's edge, where gTown.Contains still said outside). in town by
// either test: Contains, or the box the game itself uses to show the town (its inventory and
// town map button, PlayerTown::UpdateMainUI: PlayerTown::CheckCoordinates)
function OptPotionTown()
{
   if (!isObject(gTown) || !isObject(MainCharacter)) { return 0; }
   %p = MainCharacter.getPosition();
   return OptLordInTown() || gTown.CheckCoordinates(getWord(%p, 0), getWord(%p, 1));
}

// raids: CheckForRaiderSpawn and Encounter::dailyUpdate send 2-4 raiders from a raider fort to a
// spot about 50 m from your house; they stay 20 s + 10 s per level, steal gold and food from
// buildings and attack townsfolk within 75 m. the party leader carries a map icon (RaidIcon).
// returns "leader distanceToTown raiders rating" for the nearest party coming to town, or "".
// a party counts THE MOMENT IT EXISTS, however far away (Building::spawnRaiders is hooked and
// refreshes the guide at once): coming until it has reached the town (within 120 m) once.
// within 120 m it counts while it is there; after it has been there, leaving again is going
// home. a party far out that has not come 2 m closer in 30 s of play (stuck) stops counting,
// so a raid never holds the guide forever. no second look and no "closer than a moment ago"
// test: two refreshes close together used to drop it and the step flipped back and forth
function OptRaidThreat()
{
   if (!isObject(enemyFactionSet)) { return ""; }
   %town = gTown.getTownCenter();
   %tx = getWord(%town, 0);
   %ty = getWord(%town, 1);
   %best = 0;
   %bd = 999999;
   for (%i = 0; %i < enemyFactionSet.getCount(); %i++)
   {
      %c = enemyFactionSet.getObject(%i);
      if (!isObject(%c) || %c.RaidIcon $= "" || %c.getState() $= "Dead") { continue; }
      %p = %c.getPosition();
      %dx = getWord(%p, 0) - %tx;
      %dy = getWord(%p, 1) - %ty;
      %d = mSqrt(%dx * %dx + %dy * %dy);
      %id = %c.getId();
      %coming = 0;
      if (%d < 120)
      {
         $OptRaidArrived[%id] = 1;
         %coming = 1;
      }
      else if (!$OptRaidArrived[%id])
      {
         if ($OptRaidMin[%id] $= "" || %d < $OptRaidMin[%id] - 2)
         {
            $OptRaidMin[%id] = %d;
            $OptRaidMinT[%id] = $Opt::PlayMs;
         }
         %coming = ($Opt::PlayMs - $OptRaidMinT[%id] < 30000);
      }
      if (%coming && %d < %bd) { %bd = %d; %best = %c; }
   }
   if (!isObject(%best)) { return ""; }
   $OptRaidPos = %best.getPosition();
   // the fight is against the leader's whole party, wherever its members walk
   $OptN["E"] = 0;
   %party = %best.getParty();
   if (isObject(%party))
   {
      for (%m = 0; %m < %party.getPartySize(); %m++)
      {
         %c = %party.getMember(%m);
         if (isObject(%c) && %c.getState() !$= "Dead") { OptSetUnit("E", $OptN["E"], %c); $OptN["E"]++; }
      }
   }
   if ($OptN["E"] == 0) { OptSetUnit("E", 0, %best); $OptN["E"] = 1; }
   %n = $OptN["E"];
   %rate = getWord(OptRate(OptFight()), 0);
   return %best.getId() SPC mRound(%bd) SPC %n SPC %rate;
}

// a spot %d metres from %p, straight away from %q
function OptAwayFrom(%p, %q, %d)
{
   %dx = getWord(%p, 0) - getWord(%q, 0);
   %dy = getWord(%p, 1) - getWord(%q, 1);
   %len = mSqrt(%dx * %dx + %dy * %dy);
   if (%len < 1) { %dx = 1; %dy = 0; %len = 1; }
   return (getWord(%p, 0) + %dx / %len * %d) SPC (getWord(%p, 1) + %dy / %len * %d) SPC getWord(%p, 2);
}

// the highest-tier raider fort still standing (gZoningMap.RaiderBases, one per level tier), kept
// while David is open; 0 otherwise
function OptKeepFortPick()
{
   $OptKeepFort = 0;
   if (OptHave($Achievement_David) || MainCharacter.DifficultyLevel < 2 || !isObject(gZoningMap)) { return; }
   for (%i = 0; %i < 16; %i++)
   {
      %b = gZoningMap.RaiderBases[%i];
      if (%b !$= "" && isObject(%b) && !%b.won) { $OptKeepFort = %b.getId(); }
   }
}

// raider forts still standing (sites made by Encounter::MakeBase); each one cleared stops its raids
function OptFortsLeft()
{
   %n = 0;
   for (%i = 0; %i < $OptSiteN; %i++) { if ($OptSite[%i].canSpawn) { %n++; } }
   return %n;
}

// living enemies within %radius of %pos, loaded into group E. returns "rawLevel name" of the strongest, or ""
function OptLoadEnemiesNear(%pos, %radius)
{
   $OptN["E"] = 0;
   %top = "";
   %topL = -1;
   if (!isObject(enemyFactionSet)) { return ""; }
   for (%i = 0; %i < enemyFactionSet.getCount(); %i++)
   {
      %c = enemyFactionSet.getObject(%i);
      if (!isObject(%c) || %c.getState() $= "Dead") { continue; }
      if (OptDist2D(%c.getPosition(), %pos) > %radius) { continue; }
      OptSetUnit("E", $OptN["E"], %c);
      $OptN["E"]++;
      if (%c.Level > %topL) { %topL = %c.Level; %top = %c.Level SPC OptCName(%c); }
   }
   return %top;
}

// the best shooting weapon you own: "attack speed name", or ""
// a group of enemies near you that you can beat now, preferring SAFE, then higher level, then nearer.
// returns "x y z|rate|name|count|dist" or "". Everyone within 15m of the target is counted as joining.
function OptBestPull()
{
   %sync = $Opt::PipeSync;
   $Opt::PipeSync = 1;
   OptPullBegin();
   OptPullStep(getRealTime());
   $Opt::PipeSync = %sync;
   return $OptPullResult;
}

function OptPullBegin()
{
   %lp = MainCharacter.getPosition();
   $OptPLX = getWord(%lp, 0);
   $OptPLY = getWord(%lp, 1);
   $OptPCI = 0;
   $OptPN = 0;
   $OptPI = 0;
   $OptPullResult = "";
   $OptPBestRank = 2;
   $OptPBestDist = 99999;
   $OptPBestLvl = -1;
   $OptPBestObj = 0;
}

// phase 1 collects living enemies within 160 m (at most 40); phase 2 rates the group around
// each one (everyone within 15 m joins). stops when the piece's time is used up; 1 when done
function OptPullStep(%t0)
{
   if (!isObject(enemyFactionSet)) { return 1; }
   %cnt = enemyFactionSet.getCount();
   %done = 0;
   while ($OptPCI < %cnt && $OptPN < 40)
   {
      if (!OptSliceLeft(%t0, %done, 80)) { return 0; }
      %c = enemyFactionSet.getObject($OptPCI);
      $OptPCI++;
      %done++;
      if (!isObject(%c) || %c.getState() $= "Dead") { continue; }
      %p = %c.getPosition();
      %dx = getWord(%p, 0) - $OptPLX;
      %dy = getWord(%p, 1) - $OptPLY;
      %d2 = %dx * %dx + %dy * %dy;
      if (%d2 > 25600) { continue; }
      // a raiding party is handled by the raid step, never pulled piecemeal
      if (OptIsRaider(%c)) { continue; }
      $OptPObj[$OptPN] = %c;
      $OptPPos[$OptPN] = %p;
      $OptPX[$OptPN] = getWord(%p, 0);
      $OptPY[$OptPN] = getWord(%p, 1);
      $OptPDist[$OptPN] = mSqrt(%d2);
      $OptPN++;
   }
   %n = $OptPN;
   %done = 0;
   while ($OptPI < %n)
   {
      if (!OptSliceLeft(%t0, %done, 6)) { return 0; }
      %i = $OptPI;
      $OptPI++;
      %done++;
      if (!isObject($OptPObj[%i]) || $OptPObj[%i].getState() $= "Dead") { continue; }
      $OptN["E"] = 0;
      %x = $OptPX[%i];
      %y = $OptPY[%i];
      for (%j = 0; %j < %n; %j++)
      {
         %dx = $OptPX[%j] - %x;
         %dy = $OptPY[%j] - %y;
         if (%dx * %dx + %dy * %dy <= 225 && isObject($OptPObj[%j]) && $OptPObj[%j].getState() !$= "Dead") { OptSetUnit("E", $OptN["E"], $OptPObj[%j]); $OptN["E"]++; }
      }
      %rate = getWord(OptRate(OptFight()), 0);
      %rank = OptRankOf(%rate);
      if (%rank > 1) { continue; }
      %lvl = $OptPObj[%i].Level;
      %dist = $OptPDist[%i];
      if (%rank < $OptPBestRank || (%rank == $OptPBestRank && (%lvl > $OptPBestLvl || (%lvl == $OptPBestLvl && %dist < $OptPBestDist))))
      {
         $OptPullResult = $OptPPos[%i] @ "|" @ %rate @ "|" @ OptCName($OptPObj[%i]) @ "|" @ $OptN["E"] @ "|" @ mRound(%dist);
         $OptPBestRank = %rank; $OptPBestDist = %dist; $OptPBestLvl = %lvl;
         $OptPBestObj = $OptPObj[%i];
      }
   }
   // the target already given stays until it dies or its group is no longer SAFE or OK,
   // wherever you walk (new enemies coming into range don't replace it)
   %prev = $OptPullObj;
   if (isObject(%prev) && %prev.getState() !$= "Dead" && !OptIsRaider(%prev))
   {
      %pp = %prev.getPosition();
      OptLoadEnemiesNear(%pp, 15);
      %rate = getWord(OptRate(OptFight()), 0);
      if (OptRankOf(%rate) <= 1)
      {
         $OptPullResult = %pp @ "|" @ %rate @ "|" @ OptCName(%prev) @ "|" @ $OptN["E"] @ "|" @ mRound(OptDist2D(%pp, $OptPLX SPC $OptPLY SPC 0));
         return 1;
      }
   }
   $OptPullObj = ($OptPullResult !$= "") ? $OptPBestObj : 0;
   return 1;
}

// the site gear is scored against: the one the plan sends you to (the target search's site),
// else the first SAFE or OK site, else the weakest site. 0 when none is left
function OptGearSitePick()
{
   if ($OptSiteN < 1) { return 0; }
   %g = OptSiteIndex($OptGoSite);
   if (%g >= 0) { return $OptSite[%g]; }
   %i = $OptSiteIdx[0];
   if ($OptSiteRank[%i] <= 1) { return $OptSite[%i]; }
   %w = OptWeakestSite();
   return %w >= 0 ? $OptSite[%w] : 0;
}

// the unwon site with the fewest squares on the map (nearest on a tie): its index, or -1.
// the site already given stays until it is won or one with fewer squares exists; the raider
// fort kept for David is skipped
function OptWeakestSite()
{
   %best = -1;
   %keep = -1;
   for (%i = 0; %i < $OptSiteN; %i++)
   {
      %id = $OptSite[%i].getId();
      if (%id == $OptKeepFort && $OptSiteN > 1) { continue; }
      if (%id == $OptWeakKeep) { %keep = %i; }
      if (%best < 0 || $OptSiteDanger[%i] < $OptSiteDanger[%best] || ($OptSiteDanger[%i] == $OptSiteDanger[%best] && $OptSiteDist[%i] < $OptSiteDist[%best])) { %best = %i; }
   }
   if (%keep >= 0 && %best >= 0 && $OptSiteDanger[%keep] <= $OptSiteDanger[%best]) { %best = %keep; }
   $OptWeakKeep = %best >= 0 ? $OptSite[%best].getId() : 0;
   return %best;
}

function OptBestBow()
{
   %best = "";
   %bestA = -1;
   %eq = OptEquipped(MainCharacter, 0);
   if (isObject(%eq) && %eq.getDataBlock().ActionType $= "missile attack")
   {
      %best = %eq.getAttack() SPC %eq.getSpeed() SPC %eq.getNameString() @ " (equipped)";
      %bestA = %eq.getAttack();
   }
   %invs = 0;
   %pi = MainCharacter.getParty().GetInventory();
   if (isObject(%pi)) { %inv[%invs] = %pi; %src[%invs] = ""; %invs++; }
   %ti = gTown.GetInventory();
   if (isObject(%ti)) { %inv[%invs] = %ti; %src[%invs] = " (in town)"; %invs++; }
   for (%v = 0; %v < %invs; %v++)
   {
      %n = %inv[%v].GetNumSlots();
      for (%k = 0; %k < %n; %k++)
      {
         %it = %inv[%v].GetItem(%k);
         if (!isObject(%it)) { continue; }
         %db = %it.getDataBlock();
         if (%db.category $= "Weapon" && %db.ActionType $= "missile attack" && %it.getAttack() > %bestA)
         {
            %bestA = %it.getAttack();
            %best = %it.getAttack() SPC %it.getSpeed() SPC %it.getNameString() @ %src[%v];
         }
      }
   }
   return %best;
}

// THE DAVID TARGET (the player's rules: "AS SOON as there's an enemy in the entire map where we
// can [get the] David achievement for defeating it, that will be the mod's priority", then, sent
// at level 1 to a level 12 Ogre: "can't the mod calculate the weakest enemy in the map allowing
// us to complete the achievement", and yes to "wait for the weakest counting enemy you can
// beat"). the game's test (CreatureData::onDisabled): an enemy dies whose raw Level is more
// than the lord's + 19, on Hard. so the target is an enemy that
//   counts (raw Level > yours + 19), and
//   you beat: ANY RATING BUT NO at full HP with everyone who joins (OptLoadJoiners), and it is
//   the first to notice you on your way (OptHuntClean). RISKY counts here and nowhere else (the
//   player: "of course david achievement will never become SAFE", then yes to "RISKY too": an
//   enemy 20 raw levels up never rated SAFE or OK on his numbers, RISKY with 6 potions was the
//   best), and with EVERY POTION IN YOUR BAG counted as HP (OptDavidRate; the live fight check counts
//   them the same way and tells you when to drink). a straight fight at full HP alone never
//   rated a counting enemy beatable on his maps, yet he killed one: David is the one kill
//   worth the potions, and the Herbalists on the plan make them.
// the WEAKEST such enemy: lowest raw level, then home site nearest to town, then oldest. only
// the 8 weakest that count are rated (when none of those can be beaten a stronger one can't
// either). with no such enemy there is NO David step and the plan goes on as usual: at level 1
// the weakest that counts is a level 6 you lose to, and the strongest-first rule had sent him
// 485 m out to a level 12 Ogre, the target changing five times on the way as stronger enemies
// came into view.
// THE TARGET YOU WERE GIVEN STAYS (as for sites): while it lives, still counts, and is not NO at
// full HP, rated from where it was given. "nothing to beat" is looked at again every 5 s.
// THE WHOLE MAP: a site not seen up close has no enemies yet, only a level range and a count;
// the enemies it will spawn are estimated from those (as the scouting step does) and its
// weakest one that counts is rated alone. going there shows the real ones, rated for real.
// sets $OptDavidObj / Name / At / Raw / Pos / Site / Est / Rate; returns 1 when there is a target
// the fight loaded in groups P and E with every potion in your bag drunk: each heals a third of
// max HP (Creature::UseItem), so they fight like that much more HP, on top of full HP (%full 1)
// or your HP now (%full 0)
function OptDavidRate(%full)
{
   %L = $OptLord;
   %h0 = $OptHP["P", %L];
   $OptHP["P", %L] = (%full ? $OptMHP["P", %L] : %h0) + OptPotions() * mClamp(mFloor($OptMHP["P", %L] * 0.333), 1, 1000);
   %r = getWord(OptRate(OptFight()), 0);
   $OptHP["P", %L] = %h0;
   return %r;
}

function OptDavidSet(%c)
{
   $OptDavidName = OptCName(%c);
   $OptDavidRaw = %c.Level;
   $OptDavidPos = %c.getPosition();
   %home = %c.homeEncounter;
   if (isObject(%home))
   {
      $OptDavidAt = OptSiteName(%home);
      %hid = %home.getId();
      for (%i = 0; %i < $OptSiteN; %i++)
      {
         if (isObject($OptSite[%i]) && $OptSite[%i].getId() == %hid) { $OptDavidSite = %i; break; }
      }
   }
}

// places a candidate among the 8 weakest ($OptDK*): raw level, then how far its home site is
// from town, then id. %obj: a living enemy, or 0 with %site / %k for the estimated enemy %k of
// a site not seen
function OptDavidCand(%lv, %d, %id, %obj, %site, %k)
{
   %n = $OptDKN;
   %at = %n;
   while (%at > 0 && (%lv < $OptDKL[%at - 1] || (%lv == $OptDKL[%at - 1] && (%d < $OptDKD[%at - 1] || (%d == $OptDKD[%at - 1] && %id < $OptDKI[%at - 1])))))
   {
      %at--;
   }
   if (%at >= 8) { return; }
   for (%j = (%n < 8 ? %n : 7); %j > %at; %j--)
   {
      $OptDKL[%j] = $OptDKL[%j - 1];
      $OptDKD[%j] = $OptDKD[%j - 1];
      $OptDKI[%j] = $OptDKI[%j - 1];
      $OptDKO[%j] = $OptDKO[%j - 1];
      $OptDKS[%j] = $OptDKS[%j - 1];
      $OptDKK[%j] = $OptDKK[%j - 1];
   }
   $OptDKL[%at] = %lv;
   $OptDKD[%at] = %d;
   $OptDKI[%at] = %id;
   $OptDKO[%at] = %obj;
   $OptDKS[%at] = %site;
   $OptDKK[%at] = %k;
   if (%n < 8) { $OptDKN = %n + 1; }
}

// the estimated enemy %k of site %e (not seen up close) alone in group E; 0 when the site has
// been seen meanwhile (its real enemies are rated as living ones) or has no such enemy
function OptDavidEstLoad(%e, %k)
{
   if (!isObject(%e) || %e.won) { return 0; }
   if (OptLoadSite(%e)) { return 0; }
   if (%k >= $OptN["E"]) { return 0; }
   %a = $OptA["E", %k];
   %d = $OptD["E", %k];
   %h = $OptHP["E", %k];
   %t = $OptT["E", %k];
   %nm = $OptNm["E", %k];
   OptPutEstimate(0, %a, %d, %h, %t, %nm);
   $OptN["E"] = 1;
   return 1;
}

function OptDavidEstSet(%e, %raw, %r)
{
   $OptDavidName = "a level " @ OptShown(%raw) @ " enemy";
   $OptDavidAt = OptSiteName(%e);
   $OptDavidRaw = %raw;
   $OptDavidPos = OptSitePos(%e);
   $OptDavidEst = 1;
   $OptDavidRate = %r;
   %hid = %e.getId();
   for (%i = 0; %i < $OptSiteN; %i++)
   {
      if (isObject($OptSite[%i]) && $OptSite[%i].getId() == %hid) { $OptDavidSite = %i; break; }
   }
}

function OptDavidPick()
{
   $OptDavidName = "";
   $OptDavidAt = "";
   $OptDavidRaw = -1;
   $OptDavidPos = "";
   $OptDavidSite = -1;
   $OptDavidEst = 0;
   $OptDavidRate = "";
   if (OptHave($Achievement_David) || MainCharacter.DifficultyLevel < 2) { $OptDavidObj = 0; $OptDavidEstSite = 0; return 0; }
   // who joins is read from the target search's list of enemies: not before it exists
   if (!isObject(enemyFactionSet) || $OptQN < 1) { return 0; }
   %lord = MainCharacter.Level;
   // the target you were given stays: a living enemy
   %o = $OptDavidObj;
   if (isObject(%o) && %o.getState() !$= "Dead" && %o.Level > %lord + 19)
   {
      $OptHuntFromFix = $OptDavidFrom;
      OptLoadJoiners(%o);
      %r = OptDavidRate(1);
      $OptHuntFromFix = "";
      if (OptRankOf(%r) <= 2)
      {
         $OptDavidRate = %r;
         OptDavidSet(%o);
         return 1;
      }
   }
   $OptDavidObj = 0;
   // ... or the estimated enemy of a site you still have not seen up close
   %es = $OptDavidEstSite;
   if (isObject(%es) && $OptDavidEstRaw > %lord + 19 && OptDavidEstLoad(%es, $OptDavidEstK))
   {
      %r = OptDavidRate(1);
      if (OptRankOf(%r) <= 2)
      {
         OptDavidEstSet(%es, $OptDavidEstRaw, %r);
         return 1;
      }
   }
   $OptDavidEstSite = 0;
   %L = $OptLord;
   %key = %lord @ "_" @ $OptA["P", %L] @ "_" @ $OptD["P", %L] @ "_" @ $OptT["P", %L] @ "_" @ $OptMHP["P", %L] @ "_" @ OptPotions();
   // "none of the 8 weakest can be beaten" holds for 5 s (or until your stats or potions change)
   if ($OptDavidNoKey $= %key && getRealTime() - $OptDavidNoAt < 5000) { return 0; }
   // a paused refresh does the whole pass in one frame: the search waits for the next normal
   // pass (the target you already have was looked at above)
   if ($Opt::PipeSync) { return 0; }
   // THE WHOLE MAP (the player: "why does David only count for enemies I've seen and not
   // enemies in the whole map"): every living enemy, and for every site not seen up close the
   // enemies it will spawn (OptEstimateSite: its level range spread over its average count).
   // of a site not seen, its weakest enemy that counts stands for it. the 8 weakest of all
   %town = gTown.getTownCenter();
   $OptDKN = 0;
   %all = 0;
   %cnt = enemyFactionSet.getCount();
   for (%i = 0; %i < %cnt; %i++)
   {
      %c = enemyFactionSet.getObject(%i);
      if (!isObject(%c) || %c.getState() $= "Dead" || %c.Level <= %lord + 19) { continue; }
      %all++;
      %home = %c.homeEncounter;
      %hp = %c.getPosition();
      if (isObject(%home)) { %hp = (getWord(%home.encounterPosition, 0) + 32) SPC (getWord(%home.encounterPosition, 1) + 32) SPC 0; }
      OptDavidCand(%c.Level, OptDist2D(%town, %hp), %c.getId(), %c.getId(), 0, 0);
   }
   for (%i = 0; %i < $OptSiteN; %i++)
   {
      %e = $OptSite[%i];
      if (!isObject(%e) || $OptSiteExact[%i] || !isObject(%e.MonsterSet)) { continue; }
      %lo = getWord(%e.MonsterLevelRange, 0);
      %hi = getWord(%e.MonsterLevelRange, 1);
      if (%hi <= %lord + 19) { continue; }
      %ecnt = mRound((getWord(%e.SpawnNumberRange, 0) + getWord(%e.SpawnNumberRange, 1)) / 2);
      %first = -1;
      %flv = 0;
      for (%k = 0; %k < %ecnt; %k++)
      {
         %lvl = mClamp(mRound(%lo + (%hi - %lo) * (%k + 0.5) / %ecnt), 0, 999);
         if (%lvl <= %lord + 19) { continue; }
         %all++;
         if (%first < 0) { %first = %k; %flv = %lvl; }
      }
      if (%first < 0) { continue; }
      %hp = (getWord(%e.encounterPosition, 0) + 32) SPC (getWord(%e.encounterPosition, 1) + 32) SPC 0;
      OptDavidCand(%flv, OptDist2D(%town, %hp), %e.getId(), 0, %e.getId(), %first);
   }
   // for the David line in the panel (OptDavidLine): the one of the 8 nearest to being beaten
   %wn = "";
   %wa = "";
   %wl = 0;
   %wr = "";
   %wk = 9;
   for (%i = 0; %i < $OptDKN; %i++)
   {
      %es = $OptDKS[%i];
      if (%es)
      {
         if (!OptDavidEstLoad(%es, $OptDKK[%i])) { continue; }
         %r = OptDavidRate(1);
         if (OptRankOf(%r) > 2)
         {
            if (OptRankOf(%r) < %wk) { %wk = OptRankOf(%r); %wr = %r; %wl = $OptDKL[%i]; %wn = "a level " @ OptShown(%wl) @ " enemy"; %wa = OptSiteName(%es) @ ", not seen yet"; }
            continue;
         }
         $OptDavidEstSite = %es;
         $OptDavidEstK = $OptDKK[%i];
         $OptDavidEstRaw = $OptDKL[%i];
         OptDavidEstSet(%es, $OptDKL[%i], %r);
         return 1;
      }
      %c = $OptDKO[%i];
      if (!isObject(%c)) { continue; }
      if (%wn $= "") { %wl = %c.Level; %wn = OptCName(%c); %wa = isObject(%c.homeEncounter) ? OptSiteName(%c.homeEncounter) : ""; }
      if (!OptHuntClean(%c)) { continue; }
      OptLoadJoiners(%c);
      %r = OptDavidRate(1);
      if (OptRankOf(%r) > 2)
      {
         if (OptRankOf(%r) < %wk) { %wk = OptRankOf(%r); %wr = %r; %wl = %c.Level; %wn = OptCName(%c); %wa = isObject(%c.homeEncounter) ? OptSiteName(%c.homeEncounter) : ""; }
         continue;
      }
      $OptDavidObj = %c;
      $OptDavidFrom = OptLordInTown() ? gTown.getTownCenter() : MainCharacter.getPosition();
      $OptDavidRate = %r;
      OptDavidSet(%c);
      return 1;
   }
   $OptDavidNoKey = %key;
   $OptDavidNoAt = getRealTime();
   $OptDavidWaitN = %all + 0;
   $OptDavidWaitName = %wn;
   $OptDavidWaitAt = %wa;
   $OptDavidWaitRaw = %wl;
   $OptDavidWaitRate = %wr;
   return 0;
}

// one line in the panel while David is open and is not the step (the player asked "why isn't it
// prioritizing David" with nothing on screen saying so): what the step waits for. how many
// enemies count, and the one of the 8 weakest nearest to being beaten, with its rating
function OptDavidLine()
{
   if (OptHave($Achievement_David) || MainCharacter.DifficultyLevel < 2) { return ""; }
   // a target: the step itself (or the heal step for it) says so
   if ($OptDavidName !$= "" || $OptDavidWaitN $= "") { return ""; }
   %lord = MainCharacter.Level;
   if ($OptDavidWaitN < 1)
   {
      return $Opt::Grey @ "David: no enemy you have seen counts now (it takes raw " @ (%lord + 20) @ " or more, you are raw " @ %lord @ ")" @ $Opt::White @ "\n";
   }
   %p = OptPotions();
   %t = $Opt::Grey @ "David: " @ $OptDavidWaitN @ " enem" @ ($OptDavidWaitN == 1 ? "y counts" : "ies count") @ ", none you beat yet";
   if ($OptDavidWaitName !$= "")
   {
      %t = %t @ " · closest: " @ $OptDavidWaitName @ ($OptDavidWaitAt !$= "" ? " at " @ $OptDavidWaitAt : "") @ " (level " @ OptShown($OptDavidWaitRaw) @ ")";
      if ($OptDavidWaitRate !$= "") { %t = %t @ ", " @ OptRateColor($OptDavidWaitRate) @ $OptDavidWaitRate @ $Opt::Grey @ (%p > 0 ? " with your " @ %p @ " potion" @ (%p == 1 ? "" : "s") : " at full HP"); }
      else { %t = %t @ ", others notice you first"; }
   }
   return %t @ " · it becomes the step the moment one is RISKY or better" @ $Opt::White @ "\n";
}

// David: target, window and the bow method with arrow counts
function OptDavidText()
{
   if (OptHave($Achievement_David) || MainCharacter.DifficultyLevel < 2) { return ""; }
   %lord = MainCharacter.Level;
   %t = $Opt::Gold @ "DAVID" @ $Opt::White @ "  kill one enemy of raw level " @ (%lord + 20) @ " or more (you: raw " @ %lord @ ")\n";
   if ($OptTopIdx >= 0)
   {
      %s = $OptTopIdx;
      %e = $OptSite[%s];
      %deadline = $OptTopRaw - 20;
      %t = %t @ "  Target: " @ $OptSiteTopName[%s] @ " at " @ OptSiteName(%e) @ " (" @ OptWhere(OptSitePos(%e)) @ ")" @ ($OptSiteExact[%s] ? "" : ", estimate") @ "\n";
      if (%lord <= %deadline)
      {
         %t = %t @ "  Window: " @ (%deadline - %lord) @ " more raw levels, then it closes. Don't clear that site first.\n";
         %t = %t @ OptDavidMethod($OptSiteTopHP[%s], $OptSiteTopD[%s]);
      }
      else
      {
         %t = %t @ $Opt::Orange @ "  Site window closed (you are past raw " @ %deadline @ ")" @ $Opt::White @ "\n";
      }
   }
   // raiders scale with the day (GetRaiderLevelRange, Building::spawnRaiders)
   %apparent = mFloor(MainCharacter.DayCount / $gDayApparentCycle);
   %rmax = mRound(mRound(%apparent * $Game::BaseRaiderLevelPerDayMultiplier) * MainCharacter.RaiderDifficultyModifier);
   %need = %lord + 20;
   if (%rmax >= %need) { %t = %t @ "  Raiders reach raw " @ %rmax @ " now: a raid leader at town counts\n"; }
   else
   {
      %days = mCeil(%need / ($Game::BaseRaiderLevelPerDayMultiplier * MainCharacter.RaiderDifficultyModifier)) - %apparent;
      %t = %t @ $Opt::Grey @ "  Raiders: raw " @ %rmax @ " now, " @ %need @ " in about " @ %days @ " days (farthest fort must stand)" @ $Opt::White @ "\n";
   }
   return %t;
}

function OptDavidMethod(%hp, %d)
{
   %L = $OptLord;
   %t = "  How: pull it alone (they notice you at 12m), shoot, back off when it reaches you (each hit slows it 1-2s), past 64m from home it walks back: shoot again\n";
   %bow = OptBestBow();
   if (%bow $= "")
   {
      return %t @ $Opt::Orange @ "  No bow yet: keep the next one you find" @ $Opt::White @ "\n";
   }
   %cur = OptEquipped(MainCharacter, 0);
   %curA = isObject(%cur) ? %cur.getAttack() : 0;
   %curS = isObject(%cur) ? %cur.getSpeed() : 0;
   %A = $OptA["P", %L] - %curA + getWord(%bow, 0);
   %T = mClamp($OptT["P", %L] - %curS + getWord(%bow, 1), 0.2, 4);
   %per = OptExpDmg(%A, %d, OptAtkCount(0), OptDefCount(1), OptCritCount($OptCB["P", %L]));
   if (%per <= 0) { return %t; }
   %arrows = mCeil(%hp / %per);
   return %t @ "  " @ getWords(%bow, 2) @ ": " @ OptNum(%per) @ " dmg/arrow, about " @ %arrows @ " arrows for its " @ mRound(%hp) @ " HP\n";
}

// ------------------------------------------------------------------ the plan

// the one step shown big. %target (a position) adds the arrow and "direction, distance"
// %noRoute 1: a spot to move away to (run, keep away), never routed through a portal
function OptNow(%title, %info, %target, %noRoute)
{
   $OptNowTitle = %title;
   $OptNowInfo = %info;
   $OptNowTarget = %target;
   $OptNowNoRoute = (%noRoute $= "") ? 0 : %noRoute;
}

function OptPlan()
{
   %town = gTown.getTownCenter();
   %L = $OptLord;
   %hp = mRound($OptHP["P", %L]);
   %mhp = mRound($OptMHP["P", %L]);
   %hpFrac = %mhp > 0 ? %hp / %mhp : 1;
   %lord = MainCharacter.Level;
   %david = !OptHave($Achievement_David) && MainCharacter.DifficultyLevel >= 2;
   $OptNowSite = -1;
   $OptNowGear = 0;
   $OptNowTown = 0;
   $OptTownTP = "";
   $OptRaidInfo = "";
   $OptJunkStep = 0;
   $OptAutoAtk = "";
   $OptAutoSite = "";

   // open windows first
   if (OptVis(FulFillRequestDialog) && isObject(RequestFromKing))
   {
      %pen = RequestFromKing.mydata.requestFamePenalty;
      %after = mFloor(gTown.Fame) - %pen;
      if (OptNeedDisrespectful())
      {
         if (RequestFromKing.timeRemaining > 0) { OptNow("Close this window", "Don't fill, don't refuse yet. Refuse when it runs out in " @ RequestFromKing.timeRemaining @ " days (-" @ %pen @ " fame, you have " @ mFloor(gTown.Fame) @ ")", ""); }
         else if (%after >= 0) { OptNow("Press Refuse", "-" @ %pen @ " fame, " @ %after @ " left", ""); }
         else { OptNow("Press Refuse", $Opt::Red @ "-" @ %pen @ " fame, " @ %after @ " left. Get " @ (-%after) @ " fame within " @ OptNum(MainCharacter.LossFromFameTime / $gDayApparentCycle) @ " days (kill +1, site +5) or the game is lost", ""); }
      }
      else { OptNow("Press Fulfill if you can", "+" @ mFloor(gTown.FavorMultiplier * RequestFromKing.mydata.requestFameReward) @ " fame", ""); }
      return;
   }
   if (OptVis(TownsPersonTalkDlg))
   {
      // the lord's own panel (abilities, town info, Manor upgrade) uses the same window
      if (TownsPersonTalkDlg.talker $= "" || (isObject(TownsPersonTalkDlg.talker) && TownsPersonTalkDlg.talker.getId() == MainCharacter.getId()))
      {
         // this panel holds the house upgrade and the Town Upgrades: do them when the town plan says
         %tf = strreplace(OptTownPick(), "|", "\t");
         %kind = getField(%tf, 1);
         if (getField(%tf, 0) $= "do" && %kind $= "house" && OptVis(ManorUpgradeButton)) { OptNow("Press Yes (house upgrade)", getField(%tf, 4) @ " · " @ getField(%tf, 2) @ " gold · no effect on achievements", ""); }
         else if (getField(%tf, 0) $= "do" && %kind $= "access") { OptNow("Press " @ getWords(getField(%tf, 3), 1) @ " under Town Upgrades", getField(%tf, 4) @ " · " @ getField(%tf, 2) @ " gold", ""); }
         else if (getField(%tf, 0) $= "save" && (%kind $= "house" || %kind $= "access"))
         {
            %rk = $OptTownPickKeep > 0 ? " · " @ $OptTownPickKeep @ " of it kept for the Market route first" : "";
            OptNow("Press done", "saving for: " @ getField(%tf, 3) @ " (" @ mFloor(gTown.Gold) @ "/" @ (getField(%tf, 2) + $OptTownPickKeep) @ " gold)" @ %rk, "");
         }
         else { OptNow("Press done", "", ""); }
         return;
      }
      %lw = OptVis(TownsPersonJoinParty) && OptNeedLoneWolf();
      // the worker being swapped for a higher-level visitor (OptSwapPick): items off, then away
      if (isObject(TownsPersonTalkDlg.talker) && OptLordInTown() && OptSwapPick() && $OptSwapOld.getId() == TownsPersonTalkDlg.talker.getId())
      {
         %w = $OptSwapOld;
         for (%k = 0; %k < 4; %k++)
         {
            %x = OptEquipped(%w, %k);
            if (%x $= "") { continue; }
            OptNow("Drag " @ %x.getNameString() @ " into the town inventory or your bag", "from " @ OptHis(%w) @ " " @ OptTPSlotName(%k) @ " slot: sending " @ OptHim(%w) @ " away doesn't give it back", "");
            return;
         }
         OptSwapStart(%w, $OptSwapNew);
         OptNow("Press " @ getStringTableEntry($ID_TOWNS_PERSON_LEAVE_TOWN) @ ", then Yes", OptSwapWhy(%w, $OptSwapNew), "");
         return;
      }
      // the gear step said to take an item off this townsperson: still on them
      %tw = $OptTakeWho;
      if (%tw !$= "" && isObject(%tw) && isObject(TownsPersonTalkDlg.talker) && TownsPersonTalkDlg.talker.getId() == %tw.getId() && OptEquipped(%tw, $OptTakeSlot) == $OptTakeItem)
      {
         OptNow("Drag " @ $OptTakeItem.getNameString() @ " into your player inventory", "from " @ OptHis(%tw) @ " " @ OptTPSlotName($OptTakeSlot) @ " slot · then equip it", "");
         return;
      }
      if (isObject(TownsPersonTalkDlg.talker) && OptGivePick() && $OptGiveWho.getId() == TownsPersonTalkDlg.talker.getId())
      {
         OptNow("Drag " @ $OptGiveItem @ " onto " @ OptHis($OptGiveWho) @ " " @ $OptGiveSlot @ " slot", "from the " @ $OptGiveSrc @ " · " @ $OptGiveWhy @ (%lw ? " · don't press " @ getStringTableEntry($ID_TOWNS_PERSON_JOIN_PARTY) @ " (Lone Wolf)" : ""), "");
         return;
      }
      if (%lw) { OptNow("Don't press " @ getStringTableEntry($ID_TOWNS_PERSON_JOIN_PARTY), "anyone joining your party loses Lone Wolf", ""); }
      else if (OptVis(TownsPersonJoinParty)) { OptNow(getStringTableEntry($ID_TOWNS_PERSON_JOIN_PARTY) @ " is allowed", "", ""); }
      else { OptNow("Press done", "", ""); }
      return;
   }
   if (OptVis(LordLevelUpDialog))
   {
      %p = strreplace(OptLevelUpPick(), "|", "\t");
      OptNow("Pick " @ getField(%p, 0), getField(%p, 1), "");
      return;
   }
   if (OptVis(NewAbilityDialog))
   {
      %p = strreplace(OptAbilityPick(), "|", "\t");
      OptNow("Pick " @ getField(%p, 0), getField(%p, 1), "");
      return;
   }
   if (OptVis(ContinueGameDialog))
   {
      %m = OptMissing();
      if (%m !$= "" && OptVis(KeepPlayingButton)) { OptNow("Press Keep Playing", "10 minutes left. Still need: " @ %m, ""); }
      else { OptNow("Press End Game", "", ""); }
      return;
   }
   if (OptVis(HireCandidateDlg) && isObject(HireCandidateDlg.talker))
   {
      %c = HireCandidateDlg.talker;
      // SWAP FOR A HIGHER LEVEL (OptSwapPick): this visitor takes a lower-level worker's place
      if ($OptSwapHire !$= "" && %c.getId() == $OptSwapHire && !isObject($OptSwapHireOld))
      {
         %sc = getField(strreplace(OptHireInfo(%c), "|", "\t"), 1);
         %st = "L" @ %c.getApparentLevel() @ ", " @ %sc @ " gold · takes " @ $OptSwapHireOldName @ "'s place: " @ $OptSwapHireTxt;
         // the old worker's empty building comes down first, so its lot is free (OptSwapChore)
         %ob = $OptSwapOldBldg;
         if (%ob !$= "" && isObject(%ob) && !isObject(%ob.getOccupant()))
         {
            OptNow(getStringTableEntry($ID_NEVERMINDTEXT), "first demolish the empty " @ %ob.getNameString() @ " (click it, press Yes), then hire " @ OptHim(%c) @ " · " @ %st, "");
            return;
         }
         if (%sc <= gTown.Gold && gTown.Fame >= %c.FameRequired) { OptNow("Hire", %st, ""); }
         else { OptNow(getStringTableEntry($ID_NEVERMINDTEXT), %st @ " · not enough gold yet: keep " @ OptHim(%c), ""); }
         return;
      }
      if (OptSwapPick() && $OptSwapNew.getId() == %c.getId())
      {
         OptNow(getStringTableEntry($ID_NEVERMINDTEXT), "first send " @ $OptSwapOld.getNameString() @ " away, then hire this one · " @ OptSwapWhy($OptSwapOld, %c), "");
         return;
      }
      %info = strreplace(OptHireInfo(%c), "|", "\t");
      %can = getField(%info, 1) <= gTown.Gold && gTown.Fame >= %c.FameRequired;
      %hide = OptNeedGuardian() ? " · then make sure " @ OptHis(%c) @ " window says \"" @ getStringTableEntry($ID_TOWNS_PERSON_DEFEND) @ "\"" : "";
      // food first: a farmer while the farms, upgraded, leave under 1 food a day spare
      %plan = strreplace(OptHireFarmPlan(%c), "|", "\t");
      %best = OptBestFarmHire();
      // a visitor not hired right now gets "Stop visiting": kept with "Never mind" he only holds
      // his visitor slot for his days, and a new visitor comes only to an empty slot. ONE
      // exception (the player's rule): the NEXT townsperson the plan buys, in town but not
      // affordable yet, is kept with "Never mind" (OptIsNextHire), so he is still there when the
      // gold or fame is
      %keepTxt = " · not now, so " @ OptHis(%c) @ " slot goes to a new visitor";
      %nm = getStringTableEntry($ID_NEVERMINDTEXT);
      %days = gTown.CandidateTimeRemaining[%c.candidateNumber];
      %stayTxt = " · the next hire in your plan: keep " @ OptHim(%c) @ " until you can pay" @ (%days !$= "" && %days >= 0 ? " (" @ OptHe(%c) @ " stays " @ %days @ " more day" @ (%days == 1 ? "" : "s") @ ")" : "");
      // the first hire is a Farmer: until a food worker lives in town, every other visitor is sent
      // away, the Goodman excepted (his Hostel means more visitors, OptIsSlotsHire)
      if (!OptHasFoodWorker() && !OptIsSlotsHire(%c))
      {
         if (!OptMakesFood(%c))
         {
            OptNow("Skip (Stop visiting)", getField(%info, 2) @ " · your first hire is a Farmer: nobody else until then but the Goodman (more visitors)", "");
            return;
         }
         if (isObject(%best) && %best.getId() != %c.getId() && OptMakesFood(%best))
         {
            OptNow("Skip (Stop visiting)", getField(%info, 2) @ " · hire the " @ %best.getProfessionName() @ " visiting now instead: more food" @ %keepTxt, "");
            return;
         }
         if (%can) { OptNow("Hire", getField(%info, 2) @ " · your first hire is a food worker" @ %hide, ""); }
         else if (gTown.Fame < %c.FameRequired) { OptNow(%nm, getField(%info, 2) @ " · your first food worker, " @ (%c.FameRequired - mFloor(gTown.Fame)) @ " fame short" @ %stayTxt, ""); }
         else { OptNow(%nm, getField(%info, 2) @ " · your first food worker, " @ (getField(%info, 1) - mFloor(gTown.Gold)) @ " gold short" @ %stayTxt, ""); }
         return;
      }
      // A BETTER KIND OF FOOD WORKER IS NEVER SENT AWAY (the player: a Farmer visiting with 2
      // Herders in town was told "Stop visiting ... not in the town plan"). when he can't take the
      // weaker worker's place yet (gold short, or that worker is hurt: sending a hurt townsperson
      // away costs fame) he is kept with Never mind until he can
      %tw = OptTradeUpFor(%c);
      if (isObject(%tw))
      {
         %twb = %tw.getHomeBuilding();
         %tww = %tw.GetCurrentHealth() < %tw.getMaxHealth() ? %tw.getNameString() @ " must be at full HP first (sending a hurt townsperson away costs fame)" : OptShortText(%c, getField(%info, 1));
         OptNow(%nm, getField(%info, 2) @ " · wanted: takes the place of " @ %tw.getNameString() @ (isObject(%twb) ? " (" @ %twb.getNameString() @ ")" : "") @ ", the 6 food workers become Farmers one by one · " @ %tww @ (%days !$= "" && %days >= 0 ? " · " @ OptHe(%c) @ " stays " @ %days @ " more day" @ (%days == 1 ? "" : "s") : ""), "");
         return;
      }
      // A VISITOR WANTED FOR A LEVEL SWAP IS NEVER SENT AWAY (the player: a L2 Farmer visiting with a
      // L1 Farmer in town was told "Stop visiting ... not in the town plan"). when the swap can't
      // happen yet (gold or fame short, or the worker hurt) he is kept with Never mind
      %lw = OptLevelSwapFor(%c);
      if (isObject(%lw))
      {
         %lww = %lw.GetCurrentHealth() < %lw.getMaxHealth() ? %lw.getNameString() @ " must be at full HP first (sending a hurt townsperson away costs fame)" : OptShortText(%c, %c.HireCost);
         OptNow(%nm, getField(%info, 2) @ " · wanted: takes the place of " @ %lw.getNameString() @ " (L" @ %lw.getApparentLevel() @ "), the same work with more HP and defense against raiders · " @ %lww @ (%days !$= "" && %days >= 0 ? " · " @ OptHe(%c) @ " stays " @ %days @ " more day" @ (%days == 1 ? "" : "s") : ""), "");
         return;
      }
      // FOOD WORKERS UP TO 6 (OptFoodShort): while fewer than 6 food workers live in town, every
      // food worker who visits is hired, whatever the food. with one place left and a better one
      // visiting (a better kind first, OptBestFarmHire), that one
      if (OptFarmerWanted(%c))
      {
         %fn = OptFoodWorkers();
         if (isObject(%best) && %best.getId() != %c.getId() && %fn + 1 >= $Opt::Farmers)
         {
            OptNow("Skip (Stop visiting)", getField(%info, 2) @ " · hire the " @ %best.getProfessionName() @ " visiting now instead: more food" @ %keepTxt, "");
            return;
         }
         %fplan = (%plan !$= "") ? " · then " @ (OptHasManor() ? "" : "Manor + ") @ getField(%plan, 2) @ ": +" @ OptNum(getField(%plan, 0)) @ " food a day" : "";
         %ftxt = " · food worker " @ (%fn + 1) @ " of " @ $Opt::Farmers @ ": every food worker is hired until there are " @ $Opt::Farmers @ ", then traded up to Farmers";
         if (%can) { OptNow("Hire", getField(%info, 2) @ %fplan @ %ftxt @ %hide, ""); }
         else { OptNow(%nm, getField(%info, 2) @ %ftxt @ " · " @ OptShortText(%c, getField(%info, 1)) @ %stayTxt, ""); }
         return;
      }
      if (OptFoodNetPlanned() < 1 && isObject(%best) && %c.getId() == %best.getId())
      {
         %planText = (%plan !$= "") ? " · then " @ (OptHasManor() ? "" : "Manor + ") @ getField(%plan, 2) @ ": +" @ OptNum(getField(%plan, 0)) @ " food a day" : "";
         %also = OptOtherWantedVisitor(%c);
         if (%can) { OptNow("Hire", getField(%info, 2) @ %planText @ (%also !$= "" ? " · then hire the " @ %also @ " visiting now too" : "") @ %hide, ""); }
         else { OptNow(%nm, getField(%info, 2) @ " · wanted for food, " @ OptShortText(%c, getField(%info, 1)) @ %stayTxt, ""); }
         return;
      }
      // the town plan: this profession's line wants a worker and the farms can feed one more.
      // a farm worker visiting at the same time counts: hire both when the gold covers both
      %l = OptTownWants(%c);
      %foodOk = OptFoodNetPlanned() >= 1;
      // the Goodman for more visitors eats from the food in store (OptIsSlotsHire)
      if (!%foodOk && OptIsSlotsHire(%c)) { %foodOk = 1; }
      %farmTxt = "";
      if (%l >= 0 && !%foodOk && isObject(%best) && %best.getId() != %c.getId())
      {
         %bi = strreplace(OptHireInfo(%best), "|", "\t");
         %bnet = getField(%bi, 3);
         %bp = strreplace(OptHireFarmPlan(%best), "|", "\t");
         if (%bp !$= "" && getField(%bp, 0) > %bnet) { %bnet = getField(%bp, 0); }
         if (OptFoodNetPlanned() + %bnet >= 1 && getField(%bi, 1) + getField(%info, 1) <= gTown.Gold)
         {
            %foodOk = 1;
            %farmTxt = " · hire the " @ %best.getProfessionName() @ " visiting now too: " @ OptHe(%best) @ " feeds this one (" @ (getField(%bi, 1) + getField(%info, 1)) @ " gold for both)";
         }
      }
      // a hire for a line after the Market route only from the gold above what that route costs
      %keepG = (%l >= 0 && !OptLineIsMarketRoute(%l)) ? OptMarketReserve() : 0;
      if (%l >= 0 && %foodOk && %can && %keepG > 0 && gTown.Gold - %keepG < getField(%info, 1))
      {
         // the gold is kept for hires ahead of him in the plan. he stays (Never mind) only when
         // he is next too AND the Market route's own visitors are in town now (OptMarketRouteHere;
         // the player: "keeping her visiting clogs 1 slot where the merchant could appear". the
         // Market line waiting for its Inn let OptIsNextHire call her next with no Merchant there)
         if (OptIsNextHire(%c) && OptMarketRouteHere())
         {
            OptNow(%nm, getField(%info, 2) @ " · your gold goes to the Market route first (Goodman, Inn, Market, Merchant: " @ %keepG @ " gold), they are in town, then " @ OptHim(%c) @ %stayTxt, "");
            return;
         }
         OptNow("Skip (Stop visiting)", getField(%info, 2) @ " · wanted later: your gold goes to the Market route first (Goodman, Inn, Market, Merchant: " @ %keepG @ " gold), it pays " @ OptNum(OptBGold(dbBuildingInn, 0) + OptBGold(dbBuildingMarket, 0)) @ " gold a day" @ %keepTxt, "");
         return;
      }
      // two wanted visitors and gold for only one: the one higher in the plan first
      %ahead = (%l >= 0 && %foodOk && %can && %farmTxt $= "") ? OptVisitorAhead(%c, getField(%info, 1)) : "";
      if (%ahead !$= "")
      {
         OptNow("Skip (Stop visiting)", getField(%info, 2) @ " · hire the " @ getField(%ahead, 0) @ " visiting now instead (" @ getField(%ahead, 1) @ " gold, higher in the plan): your gold doesn't cover both" @ %keepTxt, "");
         return;
      }
      if (%l >= 0 && %foodOk)
      {
         // the plan's text already says to set the new hire to hide
         if (%can) { OptNow("Hire", $OptWantInfo @ %farmTxt, ""); }
         else if (OptIsNextHire(%c)) { OptNow(%nm, getField(%info, 2) @ " · wanted, " @ OptShortText(%c, getField(%info, 1)) @ %stayTxt, ""); }
         else { OptNow("Skip (Stop visiting)", getField(%info, 2) @ " · wanted, " @ OptShortText(%c, getField(%info, 1)) @ %keepTxt, ""); }
         return;
      }
      if (%l >= 0) { OptNow("Skip (Stop visiting)", getField(%info, 2) @ " · wanted, but the farms can't feed another mouth yet: hire a Farmer first" @ %keepTxt, ""); }
      else if ($OptWantLater !$= "") { OptNow("Skip (Stop visiting)", getField(%info, 2) @ " · not yet: " @ $OptWantLater @ " · until then " @ OptHe(%c) @ " would only make what you already get, and eat", ""); }
      else { OptNow("Skip (Stop visiting)", getField(%info, 2) @ " · not in the town plan", ""); }
      return;
   }
   if (OptVis(LevelGainButton))
   {
      OptNow("Click the level-up button", "", "");
      return;
   }

   // a fight you are in and losing: a potion if that turns it, else run
   %nOn = OptLoadLiveFight();
   if (%nOn > 0)
   {
      %lr = getWord(OptRate(OptFight()), 0);
      // THE DAVID FIGHT is fought at RISKY (the player's rule, see OptDavidPick): with the David
      // target among the enemies on you, the bar here is "any rating but NO" too. without this
      // the guide sent you to a RISKY David fight and said "Run, you lose" the moment it began
      %lbar = 1;
      if (%david && isObject($OptDavidObj) && $OptDavidObj.getState() !$= "Dead" && $OptDavidObj.Level > %lord + 19)
      {
         %did = $OptDavidObj.getId();
         for (%lk = 0; %lk < $OptN["E"]; %lk++)
         {
            if (isObject($OptObj["E", %lk]) && $OptObj["E", %lk].getId() == %did) { %lbar = 2; break; }
         }
      }
      if (OptRankOf(%lr) > %lbar)
      {
         // EVERY potion in your bag counts (the player's rule: 6 potions can win a fight one
         // can't). each is drunk when you are down by a potion's worth (Creature::UseItem heals a
         // third of max HP), so k potions fight like k x that much more HP. the fewest that win it.
         // in town none (the player's rule: "from the moment I enter town, drink a potion should
         // NEVER appear", OptPotionTown)
         %lpots = OptPotionTown() ? 0 : OptPotions();
         %lheal = mClamp(mFloor(%mhp * 0.333), 1, 1000);
         %after = "";
         %need = 0;
         %h0 = $OptHP["P", %L];
         for (%pk = 1; %pk <= %lpots && %need == 0; %pk++)
         {
            $OptHP["P", %L] = %h0 + %pk * %lheal;
            %pr = getWord(OptRate(OptFight()), 0);
            if (OptRankOf(%pr) <= %lbar) { %need = %pk; %after = %pr; }
         }
         $OptHP["P", %L] = %h0;
         %on = %nOn @ " enem" @ (%nOn == 1 ? "y" : "ies") @ " on you · " @ OptRateColor(%lr) @ %lr @ $Opt::White;
         if (%need > 0)
         {
            %pt = OptRateColor(%after) @ %after @ $Opt::White @ " with " @ %need @ " of your " @ %lpots @ " potion" @ (%lpots == 1 ? "" : "s") @ " · press 1 each time you are down " @ %lheal @ " HP or more";
            if (%mhp - %hp >= %lheal) { OptNow("Press 1: drink a potion", %on @ " now, " @ %pt, ""); }
            else { OptNow("Keep fighting, potions win it", %on @ " now, " @ %pt, ""); }
            return;
         }
         // the arrow points at town (the player's rule): the safest place to run to, and you heal
         // there. by the fastest way, a portal included (no-route 2, see OptShow)
         OptNow("Run to town, you lose this fight", %on @ " · run until they turn back, then heal in town" @ (%lpots > 0 ? " · a potion won't save it" : ""), %town, 2);
         return;
      }
   }

   // a request from the king: what to do with it, as the step for 6 s once, when it arrives (the
   // line below stays). remembered in prefs, so loading a save doesn't show it again. while one is
   // open the king sends no other (Creature dailyUpdate)
   if (!isObject(RequestFromKing)) { $pref::Opt::ReqSeen = ""; $OptReqSeenAt = ""; }
   else
   {
      %rq = RequestFromKing;
      %sig = $pref::Player::Name @ "|" @ %rq.mydata @ "|" @ %rq.requestedItem;
      if ($pref::Opt::ReqSeen !$= %sig) { $pref::Opt::ReqSeen = %sig; $OptReqSeenAt = getRealTime(); }
      %fresh = $OptReqSeenAt !$= "" && getRealTime() - $OptReqSeenAt < 6000;
      %pen = %rq.mydata.requestFamePenalty;
      %days = %rq.timeRemaining;
      if (OptNeedDisrespectful())
      {
         if (%fresh)
         {
            OptNow("King's request (" @ OptReqWhat() @ "): ignore it", "never press Fulfill (Disrespectful) · while it is open no new request comes · in " @ %days @ " days its window opens by itself: press Refuse then (-" @ %pen @ " fame)", "");
            return;
         }
      }
      else if (%rq.Canfulfill() && OptReqAffordable())
      {
         OptNow("Fulfill the king's request (" @ OptReqWhat() @ ")", "click the request button at the bottom right, press Fulfill · +" @ mFloor(gTown.FavorMultiplier * %rq.mydata.requestFameReward) @ " fame · else -" @ %pen @ " fame when it runs out", "");
         return;
      }
      else if (%fresh)
      {
         OptNow("King's request (" @ OptReqWhat() @ "): you can't fill it yet", %days @ " days left · -" @ %pen @ " fame if it runs out", "");
         return;
      }
   }

   // WHICH ITEM TO WEAR: the one that costs you the least damage over every enemy left on the map
   // (OptWholePick), before anything else. the same answer wherever you stand and whatever the
   // next target is; never scored against the next fight alone
   $OptTakeWho = "";
   if (OptWholePick())
   {
      $OptNowGear = 1;
      %wi = $OptWPItem;
      %wn = %wi.getNameString();
      %wsn = OptSlotName(MainCharacter, $OptWPSlot);
      %gain = OptNum(-$OptWPSum) @ " less damage taken over the " @ $OptWGPubEnemies @ " enemies left on the map";
      // each new pick goes to console.log with its numbers, so an odd one can be checked
      %pick = %wn @ ($OptWPRm ? " off " : " on ") @ %wsn;
      if (%pick !$= $Opt::LoggedPick)
      {
         $Opt::LoggedPick = %pick;
         echo("Opt gear pick: " @ %pick @ ": " @ %gain @ " (" @ $OptWGPubBeat @ " of them beatable now)");
      }
      if ($OptWPRm)
      {
         OptNow("Unequip " @ %wn, %wsn @ " slot · " @ %gain, "");
         return;
      }
      %wcur = OptEquipped(MainCharacter, $OptWPSlot);
      %gi = %wsn @ " slot" @ (%wcur !$= "" ? ", replaces " @ %wcur.getNameString() : ", empty now") @ " · " @ %gain;
      // worn by a townsperson: take it off them first; the next step equips it
      %who = $OptWPWho;
      if (%who !$= "")
      {
         $OptTakeWho = %who;
         $OptTakeSlot = $OptWPWhoSlot;
         $OptTakeItem = %wi.getId();
         OptNow("Take " @ %wn @ " off " @ %who.getNameString(), "click " @ %who.getNameString() @ ", drag it from " @ OptHis(%who) @ " " @ OptTPSlotName($OptWPWhoSlot) @ " slot into your player inventory, then equip it · " @ %gi, %who.getPosition());
         return;
      }
      // a two-handed weapon next to your shield: the game refuses it until the shield is off
      %off = OptEquipped(MainCharacter, 2);
      if ($OptWPSlot == 0 && %off !$= "" && OptTwoHanded(%wi.getDataBlock()))
      {
         OptNow("Unequip " @ %off.getNameString(), OptSlotName(MainCharacter, 2) @ " slot, into your inventory · then equip " @ %wn @ $OptWPSrc @ ": it needs both hands, and with it instead of your " @ (%wcur !$= "" ? %wcur.getNameString() @ " and " : "") @ %off.getNameString() @ " you lose less HP · " @ %gi, "");
         return;
      }
      OptNow("Equip " @ %wn @ $OptWPSrc, %gi, "");
      return;
   }
   %pots = OptPotions();
   %heal = mClamp(mFloor(%mhp * 0.333), 1, 1000);

   // enemies at town
   %nearTown = OptDist2D(MainCharacter.getPosition(), %town) < 150;
   if (gTown.isUnderAttack() && (%nearTown || TownsPeople.getCount() > 0 || %david))
   {
      %top = OptLoadEnemiesNear(%town, 80);
      %cnt = $OptN["E"];
      %rate = getWord(OptRate(OptFight()), 0);
      if (%david && %top !$= "" && getWord(%top, 0) > %lord + 19)
      {
         OptNow("David: kill " @ getWords(%top, 1) @ " at town", "raw " @ getWord(%top, 0) @ " vs your " @ %lord @ ". One kill. Bow, back off when hurt", %town);
         return;
      }
      // enemies at town are always fought (the player's rule); against a strong group the
      // townsfolk go into hiding first
      if (%rate !$= "SAFE" && %rate !$= "OK" && OptLordInTown() && OptHideChore()) { return; }
      if (TownsPeople.getCount() > 0)
      {
         OptNow("Defend the town", OptRateColor(%rate) @ %rate @ $Opt::White @ " · " @ %cnt @ " enemies" @ (OptNeedGuardian() ? " · a dead townsperson loses Guardian" : ""), %town);
         return;
      }
      if (%nearTown)
      {
         OptNow("Fight the enemies at town", OptRateColor(%rate) @ %rate @ $Opt::White @ " · " @ %cnt @ " enemies · nobody there can die", %town);
         return;
      }
   }

   // a raiding party on its way: be home before it arrives when you can beat it
   $OptRaidInfo = "";
   %raid = OptRaidThreat();
   if (%raid !$= "")
   {
      %rd = getWord(%raid, 1);
      %rn = getWord(%raid, 2);
      %rr = getWord(%raid, 3);
      %you = mRound(OptDist2D(MainCharacter.getPosition(), %town));
      %ri = OptRateColor(%rr) @ %rr @ $Opt::White @ " · " @ %rn @ " raider" @ (%rn == 1 ? "" : "s") @ " " @ %rd @ "m from town" @ (OptLordInTown() ? "" : ", you " @ %you @ "m");
      // RAIDERS ARE MET AT TOWN (the player's rule: "when the raiders are coming I don't want to
      // attack them, I want to go to town to defend the town"). away from town the step is to go
      // to town, never to chase them in the open. in town you stay until they are at the town
      // (within 120 m of its centre, where they stage, steal and attack), then you attack them
      // there. never stuck waiting: farther out a party only counts while it keeps coming
      // (OptRaidThreat), and one standing at its staging spot is inside those 120 m. against a
      // strong party the townsfolk go into hiding first
      if (%rr !$= "SAFE" && %rr !$= "OK" && OptLordInTown() && OptHideChore()) { return; }
      if (!OptLordInTown())
      {
         OptNow("Go to town and defend it", %ri @ " · raiders are coming: be in town when they arrive, don't chase them · +1 fame for each one killed", %town);
         return;
      }
      if (%rd < 120)
      {
         OptNow("Defend the town: attack the raiders", %ri @ " · kill them before they steal gold and food, +1 fame each", $OptRaidPos);
         return;
      }
      OptNow("Stay in town: the raiders are coming", %ri @ " · fight them here when they arrive", "");
      return;
   }

   // food running out loses the game: under 8 days left, the next click on the farm route (hire,
   // Manor, Large Farm) comes before loot and fights. with no click to make, the plan goes on and
   // a flashing LOW FOOD line says what to do (OptFoodWarn)
   %net = OptFoodNet();
   if (%net < 0 && gTown.Food / (-%net) < 8 && OptFoodRoute()) { return; }

   // DAVID COMES FIRST (the player's rule, see OptDavidPick): the moment an enemy on the map
   // counts for David AND you can beat it, killing it is the step, before healing for other
   // fights, junk, town clicks, loot and every site. he missed it by ONE raw level: with the
   // window open the guide sent him to another fight (HP under 90% hid the David step) and he
   // gained two raw levels there. so with a target there is nothing in between. your HP now
   // decides go or heal as for any fight: in town you rest to 95% (free); away you go on while
   // the fight is not NO with your HP now and your potions (a scratch, or half way through
   // it, never turns you round), else you heal FOR IT, a potion before any walk to town. only the steps above
   // still come first: open windows, a fight you are losing, an item swap, enemies at town or
   // raiders coming (the player's own rule, and a raider that counts is met there), food
   // running out
   if (%david && OptDavidPick())
   {
      $OptHealMode = 0;
      %dn = "David: " @ $OptDavidName @ ($OptDavidAt !$= "" ? " at " @ $OptDavidAt : "");
      %dgo = (%hpFrac >= (OptLordInTown() ? 0.95 : 0.9));
      if (!%dgo && !OptLordInTown() && isObject($OptDavidObj))
      {
         $OptHuntFromFix = $OptDavidFrom;
         OptLoadJoiners($OptDavidObj);
         %dgo = OptRankOf(OptDavidRate(0)) <= 2;
         $OptHuntFromFix = "";
      }
      if (%dgo)
      {
         $OptNowSite = $OptDavidSite;
         %dleft = $OptDavidRaw - 20 - %lord;
         OptNow(%dn, OptRateColor($OptDavidRate) @ $OptDavidRate @ $Opt::White @ (%pots > 0 ? " with the " @ %pots @ " potion" @ (%pots == 1 ? "" : "s") @ " in your bag" : "") @ " · the weakest enemy that counts and that you beat · raw " @ $OptDavidRaw @ " vs your " @ %lord @ ($OptDavidEst ? " (estimate: the site is not seen yet, its enemies are rated for real once you are close)" : "") @ " · " @ (%dleft < 1 ? "your next raw level closes it" : "counts for " @ %dleft @ " more raw level" @ (%dleft == 1 ? "" : "s") @ " of yours") @ " · one kill · pull it alone · bow · fight nothing else first: every raw level you gain brings the end closer", $OptDavidPos);
         return;
      }
      %dwhy = "then " @ %dn;
      if (OptLordInTown())
      {
         OptNow("Rest to full HP", "HP " @ %hp @ "/" @ %mhp @ " · " @ %dwhy @ " · you heal " @ mRound(gTown.HealingRate) @ " HP every 24 seconds in town", "");
         return;
      }
      // no potion here: they are the HP this fight was rated with, and drinking one now took
      // the target away (the same fight, one potion less). resting in town is free
      OptNow("Go to town and heal", "HP " @ %hp @ "/" @ %mhp @ " · " @ %dwhy @ (%pots > 0 ? " · keep your " @ %pots @ " potion" @ (%pots == 1 ? "" : "s") @ " for that fight, it is rated with them" : " · no potion in your bag") @ " · rest there to full HP", %town);
      return;
   }

   // HEALING. where you stand and your HP decide it, in this order:
   //   in town: rest whenever you are below full (95%); it is free. shown after the town clicks
   //   away from town, 80% HP or more: NEVER sent to town, whatever happened before (a scratch)
   //   away from town, under 80% HP, at the site you are sent to (its nearest enemy within 100 m):
   //     when you live through ALL that is left of it in one go with your HP now (any rating but
   //     NO), you finish it first, then heal (the player's rule; OptFinishSite)
   //   away from town, under half HP: go to town and heal
   //   away from town, between: go to town when the fight you are sent to is worse than OK with
   //     your HP now (a potion counted as backup, never drunk ahead). that answer is held for
   //     that site, so the step doesn't flip as its rating wobbles; it is decided again when you
   //     are sent to another site, and it ends the moment you are back at 80% HP
   // $OptHealFor: what a held answer belongs to ("" under half HP, "town", or the site's id)
   %fin = (!OptLordInTown() && %hpFrac < 0.8) ? OptFinishSite() : "";
   if (OptLordInTown())
   {
      $OptHealMode = (%hpFrac < 0.95);
      $OptHealWhy = "resting in town is free";
      $OptHealFor = "town";
   }
   else if (%hpFrac >= 0.8) { $OptHealMode = 0; }
   else if (%fin !$= "") { $OptHealMode = 0; }
   else if (%hpFrac < 0.5)
   {
      $OptHealMode = 1;
      $OptHealWhy = "under half HP";
      $OptHealFor = "";
   }
   else
   {
      if ($OptHealMode && $OptHealFor !$= "" && $OptHealFor !$= $OptGoSite) { $OptHealMode = 0; }
      // a potion was drunk (or found) since the answer was held: decided again with your HP now,
      // so a second potion is only asked for when the fight still needs it
      if ($OptHealMode && %pots != $OptHealPots) { $OptHealMode = 0; }
      if (!$OptHealMode)
      {
         %bad = OptFightBadNow(%pots, %heal);
         if (%bad !$= "") { $OptHealMode = 1; $OptHealWhy = %bad; $OptHealFor = $OptGoSite; }
      }
   }
   $OptHealPots = %pots;
   if (%fin !$= "")
   {
      // %fin: "rating enemies damage site"
      %fs = $OptSite[getWord(%fin, 3)];
      %fp = OptSitePos(%fs);
      $OptAutoAtk = $OptSitePosObj;
      $OptAutoSite = %fs;
      OptNow("Finish " @ OptSiteName(%fs) @ ": attack " @ $OptSitePosName, "HP " @ %hp @ "/" @ %mhp @ " · " @ getWord(%fin, 1) @ " enem" @ (getWord(%fin, 1) == 1 ? "y" : "ies") @ " left · you live through them all with your HP now: " @ OptRateColor(getWord(%fin, 0)) @ getWord(%fin, 0) @ $Opt::White @ ", about " @ mRound(OptMax(%hp - getWord(%fin, 2), 0)) @ " HP left · then heal in town" @ ($OptKillOrder !$= "" ? "\nKill first: " @ $OptKillOrder : ""), %fp);
      return;
   }
   if ($OptHealMode && !OptLordInTown())
   {
      // a potion in your bag comes before any walk to town; one that would heal past max HP
      // means no heal step (OptDrinkInstead returns 2) and the plan goes on
      %dr = OptDrinkInstead(%hp, %mhp, $OptHealWhy);
      if (%dr == 1) { return; }
      if (%dr != 2)
      {
         if (OptOnTheWay(%hp, %mhp, %town)) { return; }
         OptNow("Go to town and heal", "HP " @ %hp @ "/" @ %mhp @ " · " @ $OptHealWhy @ OptNoPotText(%dr) @ " · rest there to full HP, then the plan goes on", %town);
         return;
      }
      $OptHealMode = 0;
   }

   // with a Market and its Merchant, in town only: sell ALL your junk in one go (your bag and the
   // town inventory) before looting and fighting. the list shrinks as you sell
   if (%raidWait $= "" && OptLordInTown() && (gTown.HasOccupiedBuilding(dbBuildingMarket) || gTown.HasOccupiedBuilding(dbBuildingOrcMarket)))
   {
      OptJunkAll(1);
      if ($OptJunkN > 0)
      {
         $OptJunkStep = 1;
         OptNow("Sell your junk: " @ $OptJunkCount @ " item" @ ($OptJunkCount == 1 ? "" : "s"), "the slots flashing red in your inventories · drag each out of the inventory and press Yes, the Merchant buys it: " @ $OptJunkText @ " · none of them makes you stronger than your gear", "");
         return;
      }
   }

   // too few free slots in your bag or the town inventory (OptKeepFree): remove the worst item there
   if (%raidWait $= "" && OptFullInvStep()) { return; }

   // in town: protect and set up the townsfolk, make room, keep a spare food a day, then the town
   // plan. all of it is clicks right here, so it comes before walking off to loot
   if (OptLordInTown())
   {
      if (OptTownChores()) { return; }
      if (OptGivePick())
      {
         OptNow("Give " @ $OptGiveItem @ " to " @ $OptGiveWho.getNameString(), "click " @ $OptGiveWho.getNameString() @ ", drag it from the " @ $OptGiveSrc @ " onto " @ OptHis($OptGiveWho) @ " " @ $OptGiveSlot @ " slot · " @ $OptGiveWhy, $OptGiveWho.getPosition());
         return;
      }
      if (OptTownRoomStep()) { return; }
      if (OptFoodRoute(1)) { return; }
      $OptTownTP = OptTownPick();
      %tf = strreplace($OptTownTP, "|", "\t");
      if (getField(%tf, 0) $= "do")
      {
         %pay = getField(%tf, 6);
         OptNow(getField(%tf, 3), getField(%tf, 4) @ " · " @ getField(%tf, 2) @ " gold" @ (%pay < 9999 ? " · pays back in " @ OptNum(%pay) @ " days" : ""), getField(%tf, 1) $= "upgrade" ? getField(%tf, 5) : "");
         $OptNowTown = 1;
         return;
      }
   }

   // in town and not full yet: rest (the town clicks above came first, you heal meanwhile)
   if ($OptHealMode)
   {
      OptNow("Rest to full HP", "HP " @ %hp @ "/" @ %mhp @ " · " @ $OptHealWhy @ " · you heal " @ mRound(gTown.HealingRate) @ " HP every 24 seconds in town · the next step comes at full HP", "");
      return;
   }

   // loot before fighting: the nearest chest or body worth opening that no enemy stands at. not
   // while your bag is full: the loot waits (nothing expires) and a line below says how much
   // only loot on the way to the site the target search sends you to (anywhere when it has none)
   %go = "";
   %goName = "";
   if ($OptHuntKind $= "hunt" && isObject($OptHuntObj) && $OptHuntObj.getState() !$= "Dead") { %go = $OptHuntObj.getPosition(); }
   else if ($OptHuntKind !$= "" && $OptHuntSite >= 0) { %go = OptSitePos($OptSite[$OptHuntSite]); }
   if (%go !$= "") { %goName = OptSiteName($OptSite[$OptHuntSite]); }
   // (no loot step while a David target exists: that step comes first, above)
   // THE CHEST OR BODY YOU WERE SENT TO STAYS THE LOOT STEP until it is opened (OptLootKept). the
   // tests below can stop holding while you walk there (the target site or its enemies move, so
   // "on your way" no longer fits): the step then jumped from a far chest to a site somewhere
   // else. only loot left behind that lies nearer than it comes first
   %keep = OptLootKept();
   %canLoot = (%raidWait $= "" && OptFreeSlots() > 0);
   // loot left behind first (bodies, anything at or near a site you have won), wherever it is:
   // it comes before the next site, nearest first
   %li = %canLoot ? OptLootPick("", 1) : -1;
   if (%li >= 0 && (%keep < 0 || %li == %keep || $OptLootDist[%li] < $OptLootDist[%keep]))
   {
      if (%keep < 0) { $OptLootKeepObj = $OptLootObj[%li]; }
      %lk = $OptLootKind[%li];
      OptNow(%lk $= "body" ? "Loot the body" : "Open the chest", $OptLootWhat[%li] @ " · left behind" @ ($OptLootLostAt[%li] !$= "" ? " " @ $OptLootLostAt[%li] : "") @ ", before the next site", $OptLootPos[%li]);
      return;
   }
   if (%canLoot && %keep >= 0)
   {
      %lk = $OptLootKind[%keep];
      %kt = " · you were sent to it" @ (%goName !$= "" ? ", then on to " @ %goName : "");
      if ($OptLootLost[%keep]) { %kt = " · left behind" @ ($OptLootLostAt[%keep] !$= "" ? " " @ $OptLootLostAt[%keep] : "") @ ", before the next site"; }
      OptNow(%lk $= "body" ? "Loot the body" : "Open the chest", $OptLootWhat[%keep] @ %kt, $OptLootPos[%keep]);
      return;
   }
   %li = %canLoot ? OptLootPick(%go, 0) : -1;
   if (%li >= 0)
   {
      $OptLootKeepObj = $OptLootObj[%li];
      %lk = $OptLootKind[%li];
      OptNow(%lk $= "body" ? "Loot the body" : "Open the chest", $OptLootWhat[%li] @ (%go !$= "" ? " · on your way to " @ %goName : ""), $OptLootPos[%li]);
      return;
   }

   if ($OptSiteN < 1)
   {
      OptNow("All sites won", "Press End Game in the win window", "");
      return;
   }

   // clearing the last site ends the game 10 minutes later (startPostWinState)
   %missing = OptMissing();
   if ($OptSiteN == 1 && %missing !$= "")
   {
      if (OptWandererStep("XP while " @ OptSiteName($OptSite[0]) @ " waits (last site, need first: " @ %missing @ ")")) { return; }
      OptNow("Don't clear " @ OptSiteName($OptSite[0]) @ " yet", "last site. Need first: " @ %missing @ " · fight raiders and wanderers for XP", "");
      return;
   }

   // healing was decided above (one rule): here you are full, or away from town with a fight that
   // is SAFE or OK with your HP now
   %restFirst = 0;

   // ONE rule picks where you fight (the target search, pipeline stage "hunt"): among the sites
   // you can beat at full HP (whole, or one enemy at a time counting the ones that join) the one
   // with the fewest squares, then the nearest, walking or through portals. it stays your site
   // until it is won, or until even at full HP nothing in it can be beaten. a site with more
   // squares comes up only when no site with fewer can be beaten. full HP is used to pick,
   // so a scratch never moves you to another site; your HP now decides fight now or heal first.
   // (the steps above, danger, raids, food, HP, loot and town, still come first)
   %k = $OptHuntKind;
   %s = $OptHuntSite;
   if (%k $= "scout" && %s >= 0)
   {
      OptScoutStep(%s);
      return;
   }
   // a potion in the bag is backup, never drunk before a fight: if the fight is SAFE or OK with
   // one potion's worth of HP, you go now, and the live-fight check says "drink" the moment you
   // would lose without it (often you never need it)
   if (%k $= "clear" && %s >= 0)
   {
      %r = $OptSiteRate[%s];
      if (%r $= "SAFE" || (%r $= "OK" && !%restFirst)) { OptSiteStep(%s, %pots, %david, %lord, ""); return; }
      if (%pots > 0 && !OptLordInTown())
      {
         OptLoadSite($OptSite[%s]);
         if (OptRankOf(OptRateWithHeal(%heal)) <= 1) { OptSiteStep(%s, %pots, %david, %lord, "\nGo now, don't drink first: " @ %pots @ " potion" @ (%pots == 1 ? "" : "s") @ " as backup, the guide says \"drink\" only if the fight turns bad"); return; }
      }
      // NEVER AN ATTACK RATED WORSE THAN OK (the player's rule, after he died at Quarry: "Attack"
      // was given for a fight rated NO at full HP, let through by "a scratch (80% HP or more)
      // never sends you back", and with HP below that a heal that overshot showed the fight too).
      // worse than OK even at full HP now: hold back, no heal fixes it
      OptLoadSite($OptSite[%s]);
      %rf = OptRateFull();
      if (OptRankOf(%rf) > 1) { OptHoldBack(OptSiteName($OptSite[%s]), %rf, $OptN["E"]); return; }
      // OK at full HP: heal first (rest in town, potions that get you there, or the walk)
      if (OptHealFirst(OptSiteName($OptSite[%s]), %r, %hp, %mhp, %town)) { return; }
      // a potion would heal past your max HP: the walk to town
      if (OptOnTheWay(%hp, %mhp, %town)) { return; }
      OptNow("Go to town and heal", "HP " @ %hp @ "/" @ %mhp @ " · " @ OptSiteName($OptSite[%s]) @ " is " @ OptRateColor(%r) @ %r @ $Opt::White @ " with your HP now, " @ %rf @ " at full HP · no potion: it would heal past your max HP", %town);
      return;
   }
   if (%k $= "hunt" && %s >= 0 && isObject($OptHuntObj) && $OptHuntObj.getState() !$= "Dead")
   {
      // rated again with your HP now (the search used full HP)
      $OptHuntRate = OptHuntRate($OptHuntObj);
      %r = $OptHuntRate;
      if (%r $= "SAFE" || (%r $= "OK" && !%restFirst)) { OptHuntShow(0, %hp, %mhp); return; }
      if (%pots > 0 && !OptLordInTown())
      {
         OptLoadJoiners($OptHuntObj);
         if (OptRankOf(OptRateWithHeal(%heal)) <= 1)
         {
            OptHuntShow(0, %hp, %mhp);
            $OptNowInfo = $OptNowInfo @ "\nGo now, don't drink first: " @ %pots @ " potion" @ (%pots == 1 ? "" : "s") @ " as backup, the guide says \"drink\" only if the fight turns bad";
            return;
         }
      }
      // NEVER AN ATTACK RATED WORSE THAN OK (see the clear step above)
      %rf = OptHuntRateFull($OptHuntObj);
      %hw = OptCName($OptHuntObj) @ " at " @ OptSiteName($OptSite[%s]);
      if (OptRankOf(%rf) > 1) { OptHoldBack(%hw, %rf, $OptHuntN); return; }
      if (OptHealFirst(%hw, %r, %hp, %mhp, %town)) { return; }
      if (OptOnTheWay(%hp, %mhp, %town)) { return; }
      OptNow("Go to town and heal", "HP " @ %hp @ "/" @ %mhp @ " · " @ %hw @ " is " @ OptRateColor(%r) @ %r @ $Opt::White @ " with your HP now, " @ %rf @ " at full HP · no potion: it would heal past your max HP", %town);
      return;
   }
   // nothing beats with the HP you have now (the search rates with it): away from town and hurt,
   // heal first; an easy kill on the way still comes first (OptOnTheWay)
   if (!OptLordInTown() && %hp < %mhp)
   {
      if (OptOnTheWay(%hp, %mhp, %town)) { return; }
      OptNow("Go to town and heal", "HP " @ %hp @ "/" @ %mhp @ " · no site can be beaten with your HP now" @ (OptPotions() > 0 ? " and your potions" : "") @ " · rest there to full HP, then the plan goes on", %town);
      return;
   }
   if (OptWandererStep("no site can be beaten yet: XP from it")) { return; }
   OptNow("Nothing here you can beat yet", "no site has an enemy you beat even at full HP, counting the ones that join · get stronger gear or a level first", "");
}

// ON YOUR WAY TO TOWN (the player: "the mod is guiding me to go to town and heal when i can still
// kill a couple of mobs before reaching town safely"; he had just killed a wolf on the way). before
// any walk to town to heal: the nearest living enemy along the way (ahead of you, within 30 m of
// the straight line to town, no farther than town) that is SAFE with your HP now, counting the
// ones that join (OptHuntRate; the 4 nearest are rated). never while David is open: every level
// you gain closes it. 1 with the step
function OptOnTheWay(%hp, %mhp, %town)
{
   if (!OptHave($Achievement_David) && MainCharacter.DifficultyLevel >= 2) { return 0; }
   if (!isObject(enemyFactionSet) || %town $= "") { return 0; }
   %p = MainCharacter.getPosition();
   %px = getWord(%p, 0);
   %py = getWord(%p, 1);
   %dx = getWord(%town, 0) - %px;
   %dy = getWord(%town, 1) - %py;
   %len2 = %dx * %dx + %dy * %dy;
   if (%len2 < 100) { return 0; }
   %len = mSqrt(%len2);
   %n = 0;
   for (%i = 0; %i < enemyFactionSet.getCount(); %i++)
   {
      %e = enemyFactionSet.getObject(%i);
      if (!isObject(%e) || %e.getState() $= "Dead") { continue; }
      %ep = %e.getPosition();
      %ex = getWord(%ep, 0) - %px;
      %ey = getWord(%ep, 1) - %py;
      %t = (%ex * %dx + %ey * %dy) / %len2;
      if (%t < 0 || %t > 1) { continue; }
      if (mAbs(%ex * %dy - %ey * %dx) / %len > 30) { continue; }
      %cand[%n] = %e;
      %dist[%n] = mSqrt(%ex * %ex + %ey * %ey);
      %n++;
   }
   for (%try = 0; %try < 4 && %try < %n; %try++)
   {
      %b = -1;
      for (%k = 0; %k < %n; %k++)
      {
         if (%used[%k]) { continue; }
         if (%b < 0 || %dist[%k] < %dist[%b]) { %b = %k; }
      }
      %used[%b] = 1;
      %e = %cand[%b];
      %r = OptHuntRate(%e);
      if (%r !$= "SAFE") { continue; }
      OptNow("Kill " @ OptCName(%e) @ " on your way to town", OptRateColor(%r) @ %r @ $Opt::White @ " with HP " @ %hp @ "/" @ %mhp @ " · " @ ($OptHuntN <= 1 ? "nobody else joins" : $OptHuntN - 1 @ " more join") @ " · it is on your way, then heal in town", %e.getPosition());
      return 1;
   }
   return 0;
}

// a target worse than OK even at full HP right now (more enemies join it from where you are than
// when the target search picked it): no attack and no heal step (a heal can't make it better).
// the search picks again on its next pass
function OptHoldBack(%what, %rf, %n)
{
   OptNow("Don't attack " @ %what, OptRateColor(%rf) @ %rf @ $Opt::White @ " even at full HP right now · " @ %n @ " enem" @ (%n == 1 ? "y" : "ies") @ " in that fight · keep your distance, the guide picks another target", "");
}

// your site is fine at full HP but not with your HP now, even with a potion as backup: rest in
// town, or go there to heal (no site switching for a scratch, and no potion drunk ahead).
// 1 when a step was given; 0 when a potion would heal past max HP (no heal step, see
// OptDrinkInstead): the caller shows the fight
function OptHealFirst(%what, %rate, %hp, %mhp, %town)
{
   // the same healing rule (see OptPlan): held for this site
   $OptHealMode = 1;
   $OptHealWhy = %what @ " is " @ %rate @ " with your HP now";
   $OptHealFor = $OptGoSite;
   if (OptLordInTown())
   {
      OptNow("Rest to full HP", "HP " @ %hp @ "/" @ %mhp @ " · then " @ %what @ " · " @ OptRateColor(%rate) @ %rate @ $Opt::White @ " with your HP now", "");
      return 1;
   }
   // a potion in your bag comes before any walk to town
   %dr = OptDrinkInstead(%hp, %mhp, $OptHealWhy);
   if (%dr == 1) { return 1; }
   if (%dr == 2)
   {
      $OptHealMode = 0;
      return 0;
   }
   if (OptOnTheWay(%hp, %mhp, %town)) { return 1; }
   OptNow("Go to town and heal", "HP " @ %hp @ "/" @ %mhp @ " · then back to " @ %what @ " · " @ OptRateColor(%rate) @ %rate @ $Opt::White @ " with your HP now" @ OptNoPotText(%dr), %town);
   return 1;
}

// POTIONS BEFORE TOWN (the player's rule: "why go to town and heal when I have 6 health potions").
// away from town, whenever the guide would send you to town to heal and a healing potion is in
// your bag, the step is to drink one (key 1, +33% of max HP) and no walk. one potion at a time:
// after each the need is worked out again with your HP now (see $OptHealPots in OptPlan). still
// never a potion ahead of a fight that is SAFE or OK as you are. 1 when the step was given.
// NEVER WHEN IT WOULD HEAL PAST MAX HP (the player's rule, asked at 30/40 with a 13 HP potion):
// a potion heals a third of max HP (Creature::UseItem, Healing_Potion), so it is only drunk when
// at least that much is missing. returns 2 then, and no heal step is given at all: the fight was
// judged with that potion counted, which already brings you to full HP, so neither the potion
// nor a walk to town can make it better
function OptDrinkInstead(%hp, %mhp, %why)
{
   %p = OptPotions();
   if (%p < 1 || %hp >= %mhp || OptPotionTown()) { return 0; }
   %ph = mClamp(mFloor(%mhp * 0.333), 1, 1000);
   if (%mhp - %hp < %ph) { return 2; }
   // NO WASTED POTION (the player's rule: "why tell me to drink a potion just to tell me to go
   // back to town and heal after? that's just a wasted pot"; at 4/34 his last potion, +11, left
   // him at 15/34, still under half HP, and the next step was the walk to town): a potion only
   // when ALL the potions in your bag end the healing: half HP or more AND the fight you are sent
   // to SAFE or OK with that HP (OptFightOkWith). 80% alone is not enough: at 38/47 a Minotaur
   // still RISKY, two HP lost on the way and the walk came anyway. else 3: no potion step, you
   // walk to town and keep them (OptNoPotText)
   %h = OptMin(%hp + %p * %ph, %mhp);
   if (%h < %mhp * 0.5 || !OptFightOkWith(%h - %hp)) { return 3; }
   OptNow("Press 1: drink a potion", "HP " @ %hp @ "/" @ %mhp @ " · " @ %why @ " · " @ %p @ " potion" @ (%p == 1 ? "" : "s") @ " in your bag, +" @ %ph @ " HP each · no walk back to town", "");
   return 1;
}

// 1 when the fight the target search sends you to is SAFE or OK with %add more HP (no such
// fight: 1). the same fight OptFightBadNow rates
function OptFightOkWith(%add)
{
   %k = $OptHuntKind;
   %s = $OptHuntSite;
   if (%s < 0) { return 1; }
   if (%k $= "clear")
   {
      if (OptRankOf($OptSiteRate[%s]) <= 1) { return 1; }
      OptLoadSite($OptSite[%s]);
      return OptRankOf(OptRateWithHeal(%add)) <= 1;
   }
   if (%k $= "hunt" && isObject($OptHuntObj) && $OptHuntObj.getState() !$= "Dead")
   {
      if (OptRankOf(OptHuntRate($OptHuntObj)) <= 1) { return 1; }
      OptLoadJoiners($OptHuntObj);
      return OptRankOf(OptRateWithHeal(%add)) <= 1;
   }
   return 1;
}

// the end of a "Go to town and heal" text: why no potion (%dr, OptDrinkInstead's answer)
function OptNoPotText(%dr)
{
   if (%dr != 3) { return " · no potion in your bag"; }
   %p = OptPotions();
   return " · your " @ %p @ " potion" @ (%p == 1 ? " isn't" : "s aren't") @ " enough to skip the walk: keep " @ (%p == 1 ? "it" : "them");
}

// FINISH THE SITE (the player's rule): the site you are sent to, when you stand at it (its nearest
// living enemy within 100 m) and you live through everything left of it in one go with your HP
// now (all its living enemies at once, the worst case; SAFE or OK only. it took RISKY too, and
// "Finish Quarry, RISKY, about 2 HP left" at 13/34 HP killed him and cost Immortal): "rating
// enemies damage siteIndex", else "". only sites seen up close (real enemies, not an estimate)
function OptFinishSite()
{
   %s = OptSiteIndex($OptGoSite);
   if (%s < 0) { return ""; }
   %e = $OptSite[%s];
   if (!%e.spawnPlaced || !isObject(%e.memberSet)) { return ""; }
   %pos = OptSitePos(%e);
   if ($OptSitePosName $= "" || OptDist2D(MainCharacter.getPosition(), %pos) > 100) { return ""; }
   OptLoadSite(%e);
   if ($OptN["E"] < 1) { return ""; }
   %res = OptFight();
   %r = getWord(OptRate(%res), 0);
   if (OptRankOf(%r) > 1) { return ""; }
   return %r SPC $OptN["E"] SPC getWord(%res, 0) SPC %s;
}

// the fight the target search sends you to, with your HP now: "" when it is SAFE or OK (a potion
// in the bag counted as backup), else why not ("Troll at Cave is RISKY with your HP now")
function OptFightBadNow(%pots, %heal)
{
   %k = $OptHuntKind;
   %s = $OptHuntSite;
   if (%s < 0) { return ""; }
   if (%k $= "clear")
   {
      %r = $OptSiteRate[%s];
      if (OptRankOf(%r) <= 1) { return ""; }
      if (%pots > 0)
      {
         OptLoadSite($OptSite[%s]);
         if (OptRankOf(OptRateWithHeal(%heal)) <= 1) { return ""; }
      }
      return OptSiteName($OptSite[%s]) @ " is " @ %r @ " with your HP now";
   }
   if (%k $= "hunt" && isObject($OptHuntObj) && $OptHuntObj.getState() !$= "Dead")
   {
      %r = OptHuntRate($OptHuntObj);
      if (OptRankOf(%r) <= 1) { return ""; }
      if (%pots > 0)
      {
         OptLoadJoiners($OptHuntObj);
         if (OptRankOf(OptRateWithHeal(%heal)) <= 1) { return ""; }
      }
      return OptCName($OptHuntObj) @ " at " @ OptSiteName($OptSite[%s]) @ " is " @ %r @ " with your HP now";
   }
   return "";
}

// the fight loaded in groups P and E, rated with %add more HP for the lord (one potion's worth)
function OptRateWithHeal(%add)
{
   %L = $OptLord;
   %h0 = $OptHP["P", %L];
   $OptHP["P", %L] = OptMin(%h0 + %add, $OptMHP["P", %L]);
   %r = getWord(OptRate(OptFight()), 0);
   $OptHP["P", %L] = %h0;
   return %r;
}

// the step for clearing site %i (SAFE or OK as a whole); %note is added to its text
function OptSiteStep(%i, %pots, %david, %lord, %note)
{
   %e = $OptSite[%i];
   %r = $OptSiteRate[%i];
   $OptNowSite = %i;
   %info = OptRateColor(%r) @ %r @ $Opt::White @ " · " @ $OptSiteCount[%i] @ " enemies" @ ($OptSiteExact[%i] ? "" : " (estimate)") @ " · " @ OptPct(OptMax($OptSiteLeft[%i], 0)) @ " HP left";
   if ($OptSiteOrder[%i] !$= "") { %info = %info @ "\nKill first: " @ $OptSiteOrder[%i]; }
   if (%r $= "OK") { %info = %info @ " · " @ %pots @ " potion" @ (%pots == 1 ? "" : "s") @ " (key 1)"; }
   if (%david && %i == $OptTopIdx && %lord <= $OptTopRaw - 20) { %info = %info @ "\nDavid target here: " @ $OptSiteTopName[%i]; }
   %chest = OptLootAtSite(%e);
   if (%chest !$= "") { %info = %info @ "\nChest here: " @ %chest; }
   if ($OptSiteFort[%i]) { %info = %info @ "\nRaider fort: clearing it stops the raids it sends"; }
   %info = %info @ %note;
   %sp = OptSitePos(%e);
   $OptAutoAtk = $OptSitePosObj;
   $OptAutoSite = %e;
   // at the site already: point at the nearest enemy that is left
   if ($OptSitePosName !$= "" && OptDist2D(MainCharacter.getPosition(), %sp) < 60) { OptNow("Attack " @ $OptSitePosName @ " at " @ OptSiteName(%e), %info, %sp); }
   else { OptNow("Clear " @ OptSiteName(%e) @ ": kill its enemies", %info, %sp); }
}

// the step for the enemy the hunt search chose; 0 when it only says to rest first
function OptHuntShow(%restFirst, %hp, %mhp)
{
   %c = $OptHuntObj;
   %hr = $OptHuntRate;
   %nm = OptCName(%c);
   if (%hr $= "OK" && %restFirst)
   {
      OptNow("Rest to full HP", "HP " @ %hp @ "/" @ %mhp @ " · " @ %nm @ " is OK, not SAFE", "");
      return 0;
   }
   $OptNowSite = $OptHuntSite;
   %s = $OptHuntSite;
   %info = OptRateColor(%hr) @ %hr @ $Opt::White @ " · " @ ($OptHuntN <= 1 ? "nobody else joins" : $OptHuntN - 1 @ " more join (all within " @ mRound($OptHuntR) @ "m of you when it attacks)");
   if (%s >= 0) { %info = %info @ " · " @ $OptSiteDanger[%s] @ " square" @ ($OptSiteDanger[%s] == 1 ? "" : "s") @ ", the fewest squares you can beat, one enemy at a time"; }
   %info = %info @ " · let it come to you away from the others";
   OptNow("Attack " @ %nm @ (%s >= 0 ? " at " @ OptSiteName($OptSite[%s]) : ""), %info, %c.getPosition());
   $OptAutoAtk = %c;
   $OptAutoSite = (%s >= 0) ? $OptSite[%s] : "";
   return 1;
}

// the step for walking to unscouted site %s to see its enemies
function OptScoutStep(%s)
{
   $OptNowSite = %s;
   %e = $OptSite[%s];
   OptNow("Go to " @ OptSiteName(%e), $OptSiteDanger[%s] @ " square" @ ($OptSiteDanger[%s] == 1 ? "" : "s") @ ", the fewest squares you can beat by its estimate · its enemies get rated exactly once you are close", OptSitePos(%e));
}

// what the king asks for, in plain words (the parts of SimObject::getInfoString)
function OptReqWhat()
{
   %d = RequestFromKing.mydata;
   if (%d.RequestType $= "GoldRequest") { return %d.requestedQuantity @ " gold"; }
   if (%d.RequestType $= "FoodRequest") { return %d.requestedQuantity @ " food"; }
   if (%d.RequestType $= "AccessRequest") { return getStringTableEntry(%d.requestDisplayString); }
   if (%d.RequestType $= "ItemRequest" && isObject(RequestFromKing.requestedItem)) { return getStringTableEntry(RequestFromKing.requestedItem.getDataBlock().StringID); }
   return "a request";
}

// a food request is only worth filling with 5 days of food left after it
function OptReqAffordable()
{
   %d = RequestFromKing.mydata;
   if (%d.RequestType !$= "FoodRequest") { return 1; }
   %net = OptFoodNet();
   return %net >= 0 || gTown.Food - %d.requestedQuantity >= 5 * (-%net);
}

// how far an enemy's call for help reaches on this difficulty (BringaFriendModifier: x0.9 Easy,
// x1 Normal, x1.5 Hard and Hardcore)
function OptFriendMod()
{
   %m = MainCharacter.BringaFriendModifier;
   return (%m $= "" || %m <= 0) ? 1 : %m;
}

// the enemies in a fight with you now: any enemy attacking you or your party, and any enemy within
// its call-for-help radius of you (CreatureData::onInitiateAttack: when one swings, every enemy
// within AggressionRadius x BringaFriendModifier of you joins). loaded into group E; the count
function OptLoadLiveFight()
{
   $OptN["E"] = 0;
   if (!isObject(enemyFactionSet)) { return 0; }
   %lp = MainCharacter.getPosition();
   %lx = getWord(%lp, 0);
   %ly = getWord(%lp, 1);
   %party = MainCharacter.getParty();
   %mod = OptFriendMod();
   for (%i = 0; %i < enemyFactionSet.getCount(); %i++)
   {
      %c = enemyFactionSet.getObject(%i);
      if (!isObject(%c) || %c.getState() $= "Dead") { continue; }
      %p = %c.getPosition();
      %dx = getWord(%p, 0) - %lx;
      %dy = getWord(%p, 1) - %ly;
      %d2 = %dx * %dx + %dy * %dy;
      if (%d2 > 6400) { continue; }
      %r = %c.getDataBlock().AggressionRadius * %mod;
      %t = %c.getAttackTarget();
      if (%d2 <= %r * %r || (isObject(%t) && %party.getMemberIndex(%t) >= 0))
      {
         OptSetUnit("E", $OptN["E"], %c);
         $OptN["E"]++;
      }
   }
   return $OptN["E"];
}

// the index of site %e in the site list, or -1
function OptSiteIndex(%e)
{
   if (!isObject(%e)) { return -1; }
   %id = %e.getId();
   for (%i = 0; %i < $OptSiteN; %i++) { if ($OptSite[%i].getId() == %id) { return %i; } }
   return -1;
}

// where your walk to %to comes from: the search's start (where you stand; the town centre while
// you are in town), or the exit of the last portal on the way there
function OptHuntFrom(%to)
{
   // the site already given is rated from where it was given (OptHuntFound), so who joins
   // doesn't change with every step you take toward it
   %s = ($OptHuntFromFix !$= "") ? $OptHuntFromFix : $OptHuntLP;
   if (%s $= "") { %s = MainCharacter.getPosition(); }
   if ($OptPortN > 0)
   {
      OptRoute(%s, %to);
      if ($OptRouteVia > 0) { return $OptRouteOutX SPC $OptRouteOutY SPC 0; }
   }
   return %s;
}

// %c and everyone who joins when it attacks you, from the pool $OptQ*.
// the game's rule (CreatureData::onInitiateAttack > yellForHelp): when an enemy starts to attack
// you, every enemy within AggressionRadius x BringaFriendModifier of YOUR position joins, not of
// its own spot. and you do not stand at its spot: it notices you at its AggressionRadius (the
// GoalGuard aggroRadius, 12 m) and comes at you, or stays there and shoots. so you stand
// somewhere on the last stretch of your walk to it, from the point where it notices you
// (OptHuntFrom says where the walk comes from) to its spot. everyone within the call radius of
// any point of that stretch is counted as joining (the widest call among them counts).
// counting around its spot alone left out the enemies on your side of it: three fights the
// guide called beatable were lost that way, one of them the run.
// loaded into group E; $OptHuntR is the radius used
// who joins a fight against %c. THE TARGET YOU WERE SENT TO IS RATED FROM THE SAME POINT THE
// TARGET SEARCH USED ($OptHuntCommitFrom), never from where you happen to stand on the way (the
// player: sent to Hideout as SAFE, told on arrival it was NO, then sent back to Den)
function OptLoadJoiners(%c)
{
   %was = $OptHuntFromFix;
   if (%was $= "" && !$OptHuntFromTown && $OptHuntCommitFrom !$= "" && isObject($OptHuntObj) && isObject(%c) && %c.getId() == $OptHuntObj.getId()) { $OptHuntFromFix = $OptHuntCommitFrom; }
   OptLoadJoinersAt(%c);
   $OptHuntFromFix = %was;
}

function OptLoadJoinersAt(%c)
{
   %p = %c.getPosition();
   %x = getWord(%p, 0);
   %y = getWord(%p, 1);
   %ag = %c.getDataBlock().AggressionRadius;
   %r = %ag * OptFriendMod();
   %from = OptHuntFrom(%p);
   %dx = %x - getWord(%from, 0);
   %dy = %y - getWord(%from, 1);
   %len = mSqrt(%dx * %dx + %dy * %dy);
   %k = (%len > %ag) ? %ag / %len : 1;
   // the stretch runs from (%ax, %ay) to its spot; (%sx, %sy) is its direction and length
   %ax = %x - %dx * %k;
   %ay = %y - %dy * %k;
   %sx = %x - %ax;
   %sy = %y - %ay;
   %sl2 = %sx * %sx + %sy * %sy;
   for (%pass = 0; %pass < 3; %pass++)
   {
      $OptN["E"] = 0;
      OptSetUnit("E", 0, %c);
      $OptN["E"] = 1;
      %rmax = %r;
      for (%j = 0; %j < $OptQN; %j++)
      {
         %q = $OptQObj[%j];
         if (%q == %c.getId() || !isObject(%q) || %q.getState() $= "Dead") { continue; }
         // this enemy's distance to the nearest point of the stretch
         %qx = $OptQX[%j] - %ax;
         %qy = $OptQY[%j] - %ay;
         %t = (%sl2 > 0) ? (%qx * %sx + %qy * %sy) / %sl2 : 0;
         if (%t < 0) { %t = 0; }
         if (%t > 1) { %t = 1; }
         %ex = %qx - %t * %sx;
         %ey = %qy - %t * %sy;
         if (%ex * %ex + %ey * %ey > %r * %r) { continue; }
         OptSetUnit("E", $OptN["E"], %q);
         $OptN["E"]++;
         if ($OptQR[%j] > %rmax) { %rmax = $OptQR[%j]; }
      }
      if (%rmax <= %r) { break; }
      %r = %rmax;
   }
   $OptHuntR = %r;
}

// 1 when %c is the first enemy to notice you on your straight walk to it. each enemy notices you
// at its AggressionRadius; only those within 64 m of %c are looked at (the end of the walk).
// when another one notices you more than a metre of walking sooner, the fight you get is with
// that one: the search leaves %c out and comes to the other in its own turn
function OptHuntClean(%c)
{
   %p = %c.getPosition();
   %x = getWord(%p, 0);
   %y = getWord(%p, 1);
   %from = OptHuntFrom(%p);
   %fx = getWord(%from, 0);
   %fy = getWord(%from, 1);
   %dx = %x - %fx;
   %dy = %y - %fy;
   %len = mSqrt(%dx * %dx + %dy * %dy);
   if (%len < 0.01) { return 1; }
   %ux = %dx / %len;
   %uy = %dy / %len;
   %tc = %len - %c.getDataBlock().AggressionRadius;
   if (%tc < 0) { %tc = 0; }
   %mod = OptFriendMod();
   for (%j = 0; %j < $OptQN; %j++)
   {
      %q = $OptQObj[%j];
      if (%q == %c.getId() || !isObject(%q) || %q.getState() $= "Dead") { continue; }
      %nx = $OptQX[%j] - %x;
      %ny = $OptQY[%j] - %y;
      if (%nx * %nx + %ny * %ny > 4096) { continue; }
      %qx = $OptQX[%j] - %fx;
      %qy = $OptQY[%j] - %fy;
      %along = %qx * %ux + %qy * %uy;
      %perp2 = %qx * %qx + %qy * %qy - %along * %along;
      // $OptQR is its call radius: its notice radius is that without the difficulty factor
      %ag = $OptQR[%j] / %mod;
      if (%perp2 > %ag * %ag) { continue; }
      %half = mSqrt(%ag * %ag - %perp2);
      // its circle lies wholly behind where you start
      if (%along + %half < 0) { continue; }
      %to = %along - %half;
      if (%to < 0) { %to = 0; }
      if (%to < %tc - 1) { return 0; }
   }
   return 1;
}

// WANDERERS AND RAIDERS (the player: "if they are in the map somewhere, can't we make the mod say
// kill wanderers", then "Yes, add it" to pointing at any wanderer or raider the moment one
// appears, while there is no site to fight: the last site waits for an achievement, or no site
// can be beaten). a wanderer is an enemy alive on the map in no unwon site's members: a site's
// "Wanderer" spawns roam the map (GoalWanderMap, Encounters.cs), and raiders walk home the same
// way (RaiderWanderer). the nearest one you beat with your HP now (SAFE or OK, counting the ones
// that join; the 6 nearest are rated) is the step, and auto-attack takes it. 1 with the step
function OptWandererStep(%why)
{
   if (!isObject(enemyFactionSet) || !isObject(MainCharacter)) { return 0; }
   %me = MainCharacter.getPosition();
   %n = 0;
   for (%i = 0; %i < enemyFactionSet.getCount(); %i++)
   {
      %e = enemyFactionSet.getObject(%i);
      if (!isObject(%e) || %e.getState() $= "Dead") { continue; }
      %inSite = 0;
      for (%s = 0; %s < $OptSiteN && !%inSite; %s++)
      {
         %ms = $OptSite[%s].memberSet;
         if (isObject(%ms) && %ms.isMember(%e)) { %inSite = 1; }
      }
      if (%inSite) { continue; }
      %cand[%n] = %e;
      %dist[%n] = OptDist2D(%me, %e.getPosition());
      %n++;
   }
   for (%try = 0; %try < 6 && %try < %n; %try++)
   {
      %b = -1;
      for (%k = 0; %k < %n; %k++)
      {
         if (%used[%k]) { continue; }
         if (%b < 0 || %dist[%k] < %dist[%b]) { %b = %k; }
      }
      %used[%b] = 1;
      %e = %cand[%b];
      %r = OptHuntRate(%e);
      if (OptRankOf(%r) > 1) { continue; }
      OptNow("Kill " @ OptCName(%e) @ " (wanderer)", OptRateColor(%r) @ %r @ $Opt::White @ " · " @ ($OptHuntN <= 1 ? "nobody else joins" : $OptHuntN - 1 @ " more join") @ " · it roams the map and belongs to no site · " @ %why, %e.getPosition());
      $OptAutoAtk = %e;
      $OptAutoSite = "";
      return 1;
   }
   return 0;
}

// the fight against %c and its joiners with your HP now: SAFE OK RISKY NO. $OptHuntN = enemies
function OptHuntRate(%c)
{
   OptLoadJoiners(%c);
   $OptHuntN = $OptN["E"];
   return getWord(OptRate(OptFight()), 0);
}

// full-HP rating of the fight loaded in groups P and E: the site to go to is picked as if you were
// healed, so a scratch never sends you to another site (your HP now only decides fight or heal)
function OptRateFull()
{
   for (%k = 0; %k < $OptN["P"]; %k++)
   {
      %h[%k] = $OptHP["P", %k];
      $OptHP["P", %k] = $OptMHP["P", %k];
   }
   %r = getWord(OptRate(OptFight()), 0);
   for (%k = 0; %k < $OptN["P"]; %k++) { $OptHP["P", %k] = %h[%k]; }
   return %r;
}

// the fight loaded in groups P and E rated with your lord at the search's HP ($OptHuntPlanHP,
// OptHuntBegin) and everyone else at full HP
function OptRatePlan()
{
   %L = $OptLord;
   for (%k = 0; %k < $OptN["P"]; %k++)
   {
      %h[%k] = $OptHP["P", %k];
      $OptHP["P", %k] = (%k == %L && $OptHuntPlanHP !$= "") ? OptMin($OptHuntPlanHP, $OptMHP["P", %k]) : $OptMHP["P", %k];
   }
   %r = getWord(OptRate(OptFight()), 0);
   for (%k = 0; %k < $OptN["P"]; %k++) { $OptHP["P", %k] = %h[%k]; }
   return %r;
}

// %c and its joiners at the search's HP
function OptHuntRatePlan(%c)
{
   OptLoadJoiners(%c);
   $OptHuntN = $OptN["E"];
   return OptRatePlan();
}

// %c and its joiners at full HP: SAFE OK RISKY NO
function OptHuntRateFull(%c)
{
   OptLoadJoiners(%c);
   $OptHuntN = $OptN["E"];
   return OptRateFull();
}

// THE TARGET SEARCH: one rule for where you fight. sites with the fewest squares first, at any
// distance; among those with the same squares the site you were sent to, then the nearest (route
// length, portals counted). the first one you can beat at full HP wins:
//   clear  the whole site is SAFE or OK
//   hunt   an enemy there is SAFE or OK with everyone who joins it (its nearest such enemy; the
//          enemy already given stays while it qualifies)
//   scout  not seen up close yet, and its estimate (whole, or its weakest enemy alone) qualifies
// it stays your site until won, or until even at full HP nothing in it qualifies. worked out a
// piece at a time in the plan pipeline (stage "hunt"), so it never stutters.
// results: $OptHuntKind, $OptHuntSite (index), $OptHuntObj (hunt), $OptGoSite (site id)
function OptHuntBegin()
{
   $OptHuntKind = "";
   $OptHuntObj = 0;
   $OptHuntSite = -1;
   $OptHuntRate = "";
   $OptHuntPhase = 0;
   $OptHuntPI = 0;
   $OptHuntOI = 0;
   $OptHuntRound2 = 0;
   $OptHuntBlkN = 0;
   // the enemy pool is remade in place and its count set when it is whole ($OptQN): whoever reads
   // it meanwhile (a gear scan) never sees it empty and so never misses the enemies who join
   $OptQNew = 0;
   // in town a new trip starts: the site is picked afresh, nearest to town (measured from the town
   // centre, so it doesn't change as you walk about town). once you leave, it is kept until won
   $OptHuntFromTown = OptLordInTown();
   $OptHuntLP = $OptHuntFromTown ? gTown.getTownCenter() : MainCharacter.getPosition();
   %L = $OptLord;
   // THE SEARCH RATES WITH THE HP YOU WILL FIGHT WITH (the player's rule: "pick sites with the HP
   // you have now and from where you'll arrive, so arrival never turns you back"): full HP from
   // town (you leave rested); away from town your HP now plus what the potions in your bag heal
   // (the step drinks them before a fight they make SAFE or OK, OptHealFirst)
   %mhp = $OptMHP["P", %L];
   %plan = %mhp;
   if (!$OptHuntFromTown)
   {
      %plan = $OptHP["P", %L] + OptPotions() * mClamp(mFloor(%mhp * 0.333), 1, 1000);
      if (%plan > %mhp) { %plan = %mhp; }
   }
   $OptHuntPlanHP = %plan;
   // a site or enemy found too strong at full HP is checked again only when your attack,
   // defense, swing or max HP change, or after 60 s. any such change drops everything remembered
   // (the counter in the key), also what was remembered for the stats you had before: swapping
   // an item back and forth must give the same target each time, not one from an old memory
   %sig = $OptA["P", %L] @ "_" @ $OptD["P", %L] @ "_" @ $OptT["P", %L] @ "_" @ $OptMHP["P", %L] @ "_" @ mFloor(%plan * 10 / OptMax(%mhp, 1));
   if (%sig !$= $OptHuntSigWas) { $OptHuntSigWas = %sig; $OptHuntEpoch++; }
   $OptHuntSig = %sig @ "_" @ $OptHuntEpoch;
}

// one piece of the search; 1 when it is done
function OptHuntStep(%t0)
{
   if (!isObject(enemyFactionSet)) { return 1; }
   %done = 0;
   // phase 0: the pool, every living enemy (raiders too: they answer a call for help)
   if ($OptHuntPhase == 0)
   {
      %mod = OptFriendMod();
      %cnt = enemyFactionSet.getCount();
      while ($OptHuntPI < %cnt)
      {
         if (!OptSliceLeft(%t0, %done, 60)) { return 0; }
         %c = enemyFactionSet.getObject($OptHuntPI);
         $OptHuntPI++;
         %done++;
         if (!isObject(%c) || %c.getState() $= "Dead") { continue; }
         %p = %c.getPosition();
         $OptQObj[$OptQNew] = %c.getId();
         $OptQX[$OptQNew] = getWord(%p, 0);
         $OptQY[$OptQNew] = getWord(%p, 1);
         $OptQR[$OptQNew] = %c.getDataBlock().AggressionRadius * %mod;
         $OptQNew++;
      }
      $OptQN = $OptQNew;
      $OptHuntPhase = 1;
      $OptHuntCN = 0;
   }
   // phase 1: every unwon site by route length, the site you were sent to first
   if ($OptHuntPhase == 1)
   {
      if ($OptHuntOI == 0) { OptHuntCells(); }
      %go = $OptHuntFromTown ? -1 : OptSiteIndex($OptGoSite);
      $OptHuntGo = %go;
      while ($OptHuntOI < $OptSiteN)
      {
         if (!OptSliceLeft(%t0, %done, 8)) { return 0; }
         %i = $OptHuntOI;
         $OptHuntOI++;
         %done++;
         if ($OptSite[%i].getId() == $OptKeepFort && $OptSiteN > 1) { continue; }
         %to = OptSitePos($OptSite[%i]);
         %len = OptDist2D($OptHuntLP, %to);
         if ($OptPortN > 0) { OptRoute($OptHuntLP, %to); %len -= $OptRouteSave; }
         $OptHuntLen[%i] = %len;
         $OptHuntGo = %go;
         %pos = $OptHuntCN;
         while (%pos > 0 && OptHuntBefore(%i, $OptHuntC[%pos - 1]))
         {
            $OptHuntC[%pos] = $OptHuntC[%pos - 1];
            %pos--;
         }
         $OptHuntC[%pos] = %i;
         $OptHuntCN++;
      }
      $OptHuntA = 0;
      $OptHuntMN = -1;
      $OptHuntPhase = 2;
   }
   // phase 2: site by site until one qualifies
   %now = getRealTime();
   while ($OptHuntA < $OptHuntCN)
   {
      %s = $OptHuntC[$OptHuntA];
      %e = $OptSite[%s];
      %eid = %e.getId();
      if ($OptHuntMN < 0)
      {
         if (!OptSliceLeft(%t0, %done, 4)) { return 0; }
         %done++;
         // THE SITE YOU WERE SENT TO (away from town; the player's rule, said many times: kept
         // until won or unbeatable even at full HP, never switched while you walk to it). the
         // tests that depend on where you stand right now (the way around harder sites, which
         // enemy notices you first, who joins along your walk) and the "too strong" memory
         // never take it away: only a rating of NO at full HP does. it was sent to three
         // places in a row before any was reached
         %com = (!$OptHuntFromTown && %s == $OptHuntGo);
         // NOT BEHIND A HARDER SITE (the player's rule): a site you can only reach by passing
         // through a site with more squares is left out (OptHuntBlocked). those get a second
         // round only when nothing you can reach without passing one can be beaten
         %blk = %com ? 0 : OptHuntBlocked(%s);
         if (!$OptHuntRound2 && %blk) { $OptHuntBlkN++; $OptHuntA++; continue; }
         if ($OptHuntRound2 && !%blk) { $OptHuntA++; continue; }
         %scouted = %e.spawnPlaced && isObject(%e.memberSet);
         %cnt = %scouted ? %e.memberSet.getCount() : -1;
         %bad = $OptHuntSiteBad[%eid];
         if (!%com && %bad !$= "" && getField(%bad, 1) $= $OptHuntSig @ "_" @ %cnt && %now - getField(%bad, 0) < 60000)
         {
            $OptHuntA++;
            continue;
         }
         // every site must be SAFE or OK with the search's HP, the one you were sent to too: the
         // step never gives a fight worse than OK (#151), so a site kept at RISKY only turned you
         // back on arrival
         %lim = 1;
         if (!%scouted)
         {
            // not seen up close: the estimate, whole or its weakest enemy alone
            OptLoadSite(%e);
            %ok = OptRankOf(OptRatePlan()) <= %lim;
            %n = $OptN["E"];
            for (%j = 0; %j < %n && !%ok; %j++)
            {
               OptLoadSite(%e);
               OptPutEstimate(0, $OptA["E", %j], $OptD["E", %j], $OptHP["E", %j], $OptT["E", %j], $OptNm["E", %j]);
               $OptN["E"] = 1;
               if (OptRankOf(OptRatePlan()) <= %lim) { %ok = 1; }
            }
            if (%ok) { OptHuntFound("scout", %s, 0); return 1; }
            $OptHuntSiteBad[%eid] = %now TAB $OptHuntSig @ "_" @ %cnt;
            $OptHuntA++;
            continue;
         }
         OptLoadSite(%e);
         if (OptRankOf(OptRatePlan()) <= 1) { OptHuntFound("clear", %s, 0); return 1; }
         // its living enemies, the enemy already given first, then nearest first
         %set = %e.memberSet;
         $OptHuntMN = 0;
         for (%m = 0; %m < %set.getCount(); %m++)
         {
            %c = %set.getObject(%m);
            if (!isObject(%c) || %c.getState() $= "Dead" || OptIsRaider(%c)) { continue; }
            %d = OptDist2D(%c.getPosition(), $OptHuntLP);
            if (isObject($OptHuntKeep) && %c.getId() == $OptHuntKeep.getId()) { %d = -1; }
            %pos = $OptHuntMN;
            while (%pos > 0 && %d < $OptHuntMD[%pos - 1])
            {
               $OptHuntM[%pos] = $OptHuntM[%pos - 1];
               $OptHuntMD[%pos] = $OptHuntMD[%pos - 1];
               %pos--;
            }
            $OptHuntM[%pos] = %c;
            $OptHuntMD[%pos] = %d;
            $OptHuntMN++;
         }
         $OptHuntMI = 0;
      }
      // the site you were sent to: rated from its approach point (see %com above), SAFE or OK
      %com = (!$OptHuntFromTown && %s == $OptHuntGo);
      %lim = 1;
      while ($OptHuntMI < $OptHuntMN)
      {
         %c = $OptHuntM[$OptHuntMI];
         // another enemy would notice you first on your way to this one (OptHuntClean): not it
         if (!%com && isObject(%c) && %c.getState() !$= "Dead" && !OptHuntClean(%c)) { $OptHuntMI++; continue; }
         if (isObject(%c) && %c.getState() !$= "Dead")
         {
            %id = %c.getId();
            %bad = $OptHuntBad[%id];
            if (%com || %bad $= "" || getField(%bad, 1) !$= $OptHuntSig || %now - getField(%bad, 0) >= 60000)
            {
               if (!OptSliceLeft(%t0, %done, 4)) { return 0; }
               %done++;
               $OptHuntFromFix = %com ? $OptHuntCommitFrom : "";
               %hr = OptRankOf(OptHuntRatePlan(%c));
               $OptHuntFromFix = "";
               if (%hr <= %lim) { OptHuntFound("hunt", %s, %c); return 1; }
               if (!%com) { $OptHuntBad[%id] = %now TAB $OptHuntSig; }
            }
         }
         $OptHuntMI++;
      }
      // nothing here qualifies even at full HP: remembered until your stats or its enemies change
      $OptHuntSiteBad[%eid] = %now TAB $OptHuntSig @ "_" @ %e.memberSet.getCount();
      $OptHuntA++;
      $OptHuntMN = -1;
   }
   // second round: the sites behind a harder site (a way past one beats having no target)
   if (!$OptHuntRound2 && $OptHuntBlkN > 0)
   {
      $OptHuntRound2 = 1;
      $OptHuntA = 0;
      $OptHuntMN = -1;
      return OptHuntStep(%t0);
   }
   $OptGoSite = 0;
   return 1;
}

// THE MAP AS 64 m CELLS, for the "not behind a harder site" rule. every site fills one cell (its
// encounterPosition is the cell's corner). this notes each unwon site's squares in its cell, and
// the box that all sites, the town, your portals and the search's start lie in
function OptHuntCells()
{
   $OptCellGen++;
   %sx = mFloor(getWord($OptHuntLP, 0) / 64);
   %sy = mFloor(getWord($OptHuntLP, 1) / 64);
   $OptCellSX = %sx;
   $OptCellSY = %sy;
   %x0 = %sx; %x1 = %sx; %y0 = %sy; %y1 = %sy;
   %tc = gTown.getTownCenter();
   %cx = mFloor(getWord(%tc, 0) / 64);
   %cy = mFloor(getWord(%tc, 1) / 64);
   if (%cx < %x0) { %x0 = %cx; } if (%cx > %x1) { %x1 = %cx; } if (%cy < %y0) { %y0 = %cy; } if (%cy > %y1) { %y1 = %cy; }
   %n = Encounters.getCount();
   for (%i = 0; %i < %n; %i++)
   {
      %e = Encounters.getObject(%i);
      if (!isObject(%e)) { continue; }
      %p = %e.encounterPosition;
      %cx = mFloor((getWord(%p, 0) + 32) / 64);
      %cy = mFloor((getWord(%p, 1) + 32) / 64);
      if (%cx < %x0) { %x0 = %cx; } if (%cx > %x1) { %x1 = %cx; } if (%cy < %y0) { %y0 = %cy; } if (%cy > %y1) { %y1 = %cy; }
      if (%e.won) { continue; }
      %key = %cx @ "_" @ %cy;
      $OptCellSqG[%key] = $OptCellGen;
      $OptCellSq[%key] = %e.getDangerLevel();
   }
   for (%k = 0; %k < $OptPortN; %k++)
   {
      %cx = mFloor($OptPortX[%k] / 64);
      %cy = mFloor($OptPortY[%k] / 64);
      if (%cx < %x0) { %x0 = %cx; } if (%cx > %x1) { %x1 = %cx; } if (%cy < %y0) { %y0 = %cy; } if (%cy > %y1) { %y1 = %cy; }
   }
   $OptCellX0 = %x0;
   $OptCellX1 = %x1;
   $OptCellY0 = %y0;
   $OptCellY1 = %y1;
}

// every cell you can walk to from the search's start (where you stand; the town centre while in
// town) without entering the cell of an unwon site with more than %q squares. cell to cell
// across an edge, never across a corner (that squeezes between two sites); a portal you own in
// a reached cell lets you out at its other end. marks $OptReachG[q_cell] for this search
function OptHuntFlood(%q)
{
   $OptReachDone[%q] = $OptCellGen;
   %qx[0] = $OptCellSX;
   %qy[0] = $OptCellSY;
   %qn = 1;
   $OptReachG[%q @ "_" @ $OptCellSX @ "_" @ $OptCellSY] = $OptCellGen;
   for (%h = 0; %h < %qn && %h < 2000; %h++)
   {
      %cx = %qx[%h];
      %cy = %qy[%h];
      for (%k = 0; %k < $OptPortN; %k++)
      {
         if (mFloor($OptPortX[%k] / 64) != %cx || mFloor($OptPortY[%k] / 64) != %cy) { continue; }
         %ex = mFloor($OptPortEX[%k] / 64);
         %ey = mFloor($OptPortEY[%k] / 64);
         %key = %q @ "_" @ %ex @ "_" @ %ey;
         if ($OptReachG[%key] == $OptCellGen) { continue; }
         $OptReachG[%key] = $OptCellGen;
         %qx[%qn] = %ex;
         %qy[%qn] = %ey;
         %qn++;
      }
      for (%d = 0; %d < 4; %d++)
      {
         %nx = %cx + (%d == 0 ? 1 : (%d == 1 ? -1 : 0));
         %ny = %cy + (%d == 2 ? 1 : (%d == 3 ? -1 : 0));
         if (%nx < $OptCellX0 || %nx > $OptCellX1 || %ny < $OptCellY0 || %ny > $OptCellY1) { continue; }
         %cell = %nx @ "_" @ %ny;
         %key = %q @ "_" @ %cell;
         if ($OptReachG[%key] == $OptCellGen) { continue; }
         // a site with more squares: not through it
         if ($OptCellSqG[%cell] == $OptCellGen && $OptCellSq[%cell] > %q) { continue; }
         $OptReachG[%key] = $OptCellGen;
         %qx[%qn] = %nx;
         %qy[%qn] = %ny;
         %qn++;
      }
   }
}

// where the stretch from (%fx, %fy) along (%dx, %dy), from 0 to %tmax of its length, first
// lies inside the box: that fraction, or -1 when it never does
function OptSegBox(%fx, %fy, %dx, %dy, %x0, %y0, %x1, %y1, %tmax)
{
   %t0 = 0;
   %t1 = %tmax;
   if (mAbs(%dx) < 0.0001)
   {
      if (%fx < %x0 || %fx > %x1) { return -1; }
   }
   else
   {
      %a = (%x0 - %fx) / %dx;
      %b = (%x1 - %fx) / %dx;
      if (%a > %b) { %c = %a; %a = %b; %b = %c; }
      if (%a > %t0) { %t0 = %a; }
      if (%b < %t1) { %t1 = %b; }
      if (%t0 > %t1) { return -1; }
   }
   if (mAbs(%dy) < 0.0001)
   {
      if (%fy < %y0 || %fy > %y1) { return -1; }
   }
   else
   {
      %a = (%y0 - %fy) / %dy;
      %b = (%y1 - %fy) / %dy;
      if (%a > %b) { %c = %a; %a = %b; %b = %c; }
      if (%a > %t0) { %t0 = %a; }
      if (%b < %t1) { %t1 = %b; }
      if (%t0 > %t1) { return -1; }
   }
   return %t0;
}

// THE STRAIGHT WAY (the arrow points straight at the target, it shows no way around): 1 when the
// straight line from where your walk starts (OptHuntFrom) to site %s crosses the 64 m square of
// a site not won with MORE squares, or comes within 12 m of it (they notice you at 12 m). only
// the part of the line before it enters the target's own square counts, and a harder site you
// already stand in or next to is not held against the target. the reach test alone (any way
// round, OptHuntFlood) passed a 4-square Portal that sat right behind a 7-square Fortress and
// two 6-square Caves: there was a way round, and the arrow led straight through them
function OptHuntCrosses(%s)
{
   %e = $OptSite[%s];
   %q = $OptSiteDanger[%s];
   %tp = %e.encounterPosition;
   %tx0 = getWord(%tp, 0);
   %ty0 = getWord(%tp, 1);
   %to = (%tx0 + 32) SPC (%ty0 + 32) SPC 0;
   %from = OptHuntFrom(%to);
   %fx = getWord(%from, 0);
   %fy = getWord(%from, 1);
   %dx = %tx0 + 32 - %fx;
   %dy = %ty0 + 32 - %fy;
   %tend = OptSegBox(%fx, %fy, %dx, %dy, %tx0, %ty0, %tx0 + 64, %ty0 + 64, 1);
   if (%tend < 0) { %tend = 1; }
   if (%tend <= 0) { return 0; }
   // the part of the line that counts lies in this box: a site outside it can't be crossed
   %ex = %fx + %dx * %tend;
   %ey = %fy + %dy * %tend;
   %bx0 = OptMin(%fx, %ex);
   %bx1 = OptMax(%fx, %ex);
   %by0 = OptMin(%fy, %ey);
   %by1 = OptMax(%fy, %ey);
   for (%o = 0; %o < $OptSiteN; %o++)
   {
      if (%o == %s || $OptSiteDanger[%o] <= %q || !isObject($OptSite[%o])) { continue; }
      %op = $OptSite[%o].encounterPosition;
      %x0 = getWord(%op, 0) - 12;
      %y0 = getWord(%op, 1) - 12;
      %x1 = %x0 + 88;
      %y1 = %y0 + 88;
      if (%x1 < %bx0 || %x0 > %bx1 || %y1 < %by0 || %y0 > %by1) { continue; }
      if (%fx >= %x0 && %fx <= %x1 && %fy >= %y0 && %fy <= %y1) { continue; }
      if (OptSegBox(%fx, %fy, %dx, %dy, %x0, %y0, %x1, %y1, %tend) >= 0) { return 1; }
   }
   return 0;
}

// 1 when site %s (index) can only be reached by passing through a site with more squares, or
// when the straight way to it crosses one (OptHuntCrosses)
function OptHuntBlocked(%s)
{
   if ($OptCellGen $= "" || %s < 0 || %s >= $OptSiteN || !isObject($OptSite[%s])) { return 0; }
   %q = $OptSiteDanger[%s];
   if ($OptReachDone[%q] != $OptCellGen) { OptHuntFlood(%q); }
   %p = $OptSite[%s].encounterPosition;
   %key = %q @ "_" @ mFloor((getWord(%p, 0) + 32) / 64) @ "_" @ mFloor((getWord(%p, 1) + 32) / 64);
   if ($OptReachG[%key] != $OptCellGen) { return 1; }
   // ONE ANSWER PER SITE AND PASS. the list of sites after this one (OptAfterList) asks this for
   // every site up to five times each time the panel text is made: with the straight-way check
   // inside, that was 250 checks of every site against every other site, in one frame (the
   // text piece went from 24 to 63 ms, a paused refresh from 60 to 266 ms: a hitch every 2 s).
   // the answer only changes with the sites left, where the walk starts, and the site you
   // were sent to
   %id = $OptSite[%s].getId();
   %mk = $OptCellGen @ "_" @ $OptSiteN @ "_" @ $OptHuntLP @ "_" @ $OptHuntFromFix;
   if ($OptBlkKey[%id] $= %mk) { return $OptBlkVal[%id]; }
   %r = OptHuntCrosses(%s);
   $OptBlkKey[%id] = %mk;
   $OptBlkVal[%id] = %r;
   return %r;
}

// site order: FEWEST SQUARES FIRST, at any distance (also over the site you were sent to: it
// can't flip back, squares don't change as you walk). a 4-square site is never given while a
// 3-square site you can beat is left. among sites with the same squares: the site you were
// sent to, then route length
function OptHuntBefore(%x, %y)
{
   if ($OptSiteDanger[%x] != $OptSiteDanger[%y]) { return $OptSiteDanger[%x] < $OptSiteDanger[%y]; }
   if (%x == $OptHuntGo) { return 1; }
   if (%y == $OptHuntGo) { return 0; }
   return $OptHuntLen[%x] < $OptHuntLen[%y];
}

function OptHuntFound(%kind, %s, %c)
{
   // every new target goes to console.log with the fight that made it qualify (still loaded in
   // groups P and E), so a target that makes no sense can be checked against its numbers
   %e = $OptSite[%s];
   %tk = %kind SPC %e.getId() SPC (isObject(%c) ? %c.getId() : 0);
   if (%tk !$= $Opt::LoggedTarget)
   {
      $Opt::LoggedTarget = %tk;
      %L = $OptLord;
      %why = "";
      for (%q = 0; %q < $OptN["E"] && %q < 6; %q++) { %why = %why @ (%why $= "" ? "" : ", ") @ $OptNm["E", %q] @ " A" @ mRound($OptA["E", %q]) @ " D" @ mRound($OptD["E", %q]) @ " HP" @ mRound($OptHP["E", %q]); }
      echo("Opt target: " @ %kind @ " " @ OptSiteName(%e) @ " (" @ $OptSiteDanger[%s] @ " sq, " @ (%e.spawnPlaced ? "seen" : "not seen") @ ")" @ (isObject(%c) ? " enemy " @ OptCName(%c) : "") @ " | " @ $OptN["E"] @ " enem" @ ($OptN["E"] == 1 ? "y" : "ies") @ ": " @ %why @ " | at full HP " @ OptRateFull() @ " | you A" @ $OptA["P", %L] @ " D" @ $OptD["P", %L] @ " HP" @ $OptMHP["P", %L] @ ($OptHuntRound2 ? " | behind a harder site" : ""));
   }
   $OptHuntKind = %kind;
   $OptHuntSite = %s;
   $OptHuntObj = %c;
   // a new site: the spot the search started from is where its fights are rated from while
   // you walk there (OptHuntFrom)
   if ($OptGoSite != $OptSite[%s].getId() || $OptHuntCommitFrom $= "") { $OptHuntCommitFrom = $OptHuntLP; }
   $OptGoSite = $OptSite[%s].getId();
   if (%kind $= "hunt") { $OptHuntKeep = %c; }
   // gear is scored against the fight you will take there (OptGearBegin): this enemy and everyone
   // who joins it, or the whole site
   $OptGearFocus = (%kind $= "hunt") ? %c : "";
   // found only in the second round: it lies behind a harder site (the step says so)
   $OptHuntPast = $OptHuntRound2;
}

// the whole search in one go (outside the pipeline)
function OptHuntPick()
{
   %sync = $Opt::PipeSync;
   $Opt::PipeSync = 1;
   OptHuntBegin();
   OptHuntStep(getRealTime());
   $Opt::PipeSync = %sync;
}

// ------------------------------------------------------------------ overlay text

// the step and the text below it, from the site list the pipeline just made
function OptBuild()
{
   OptPlan();
   OptBuildText();
}

// one line on gear when nothing needs changing: says so, and for each empty slot what the best
// item you own would do there (so a shield left off is a decision, not an oversight)
function OptGearLine()
{
   // from the every-enemy totals (OptWGPublish), the same numbers the equip step uses
   if ($OptNowGear || $OptSiteN < 1) { return ""; }
   // still adding up (the player: "a (Calculating gear...) check appears as well" while the best
   // gear is worked out, so an item not asked for yet is not taken for a mistake)
   // also while an item just picked up is still being added up (the player got a Leather Helm and
   // saw no calculating line: a new item leaves the finished totals standing, only adds to them)
   if (!OptWGFresh() || OptWGPending()) { return $Opt::Orange @ "Gear: (Calculating gear... " @ OptWGProgress() @ ") your items against every enemy left on the map; the equip step comes when it is done" @ $Opt::White @ "\n"; }
   %t = "Gear: best as is over the " @ $OptWGPubEnemies @ " enemies left on the map";
   for (%s = 1; %s < 4; %s++)
   {
      if (OptEquipped(MainCharacter, %s) !$= "") { continue; }
      %best = -1;
      for (%n = 0; %n < $OptWGPubN; %n++)
      {
         if ($OptWGPubRm[%n] || $OptWGPubSlot[%n] != %s || !isObject($OptWGPubItem[%n])) { continue; }
         if (%best < 0 || $OptWGPubSum[%n] < $OptWGPubSum[%best]) { %best = %n; }
      }
      if (%best < 0) { continue; }
      %d = $OptWGPubSum[%best];
      %bi = $OptWGPubItem[%best];
      %t = %t @ " · " @ OptSlotName(MainCharacter, %s) @ ": leave empty, " @ %bi.getNameString() @ " would be " @ (%d >= 0 ? "+" : "") @ OptNum(%d) @ " damage taken";
   }
   return %t @ "\n";
}

// the sites that come after the step shown, in the target search's own order: the site the search
// picked (when the step shown is something else), then fewest squares first, then nearest.
// left out: sites the search has just found too strong even at full HP (it skips them too).
// the raider fort kept for David goes last. up to 5, in $OptAfter[0..$OptAfterN-1]
function OptAfterList()
{
   $OptAfterN = 0;
   %now = getRealTime();
   if ($OptHuntKind !$= "" && $OptHuntSite >= 0 && $OptHuntSite < $OptSiteN && $OptHuntSite != $OptNowSite)
   {
      %used[$OptHuntSite] = 1;
      $OptAfter[0] = $OptHuntSite;
      $OptAfterN = 1;
   }
   while ($OptAfterN < 5)
   {
      %best = -1;
      %bq = 0;
      for (%i = 0; %i < $OptSiteN; %i++)
      {
         if (%i == $OptNowSite || %used[%i]) { continue; }
         %id = $OptSite[%i].getId();
         %bad = $OptHuntSiteBad[%id];
         if (%bad !$= "" && strstr(getField(%bad, 1), $OptHuntSig @ "_") == 0 && %now - getField(%bad, 0) < 60000) { continue; }
         // behind a site with more squares: the search leaves it out too
         if (OptHuntBlocked(%i)) { continue; }
         %q = (%id == $OptKeepFort && $OptSiteN > 1) ? 99 : $OptSiteDanger[%i];
         if (%best < 0 || %q < %bq || (%q == %bq && $OptSiteDist[%i] < $OptSiteDist[%best])) { %best = %i; %bq = %q; }
      }
      if (%best < 0) { break; }
      %used[%best] = 1;
      $OptAfter[$OptAfterN] = %best;
      $OptAfterN++;
   }
}

function OptBuildText()
{
   %L = $OptLord;
   %david = !OptHave($Achievement_David) && MainCharacter.DifficultyLevel >= 2;
   %t = "";

   // the step after this one, in the target search's own order (OptAfterList)
   OptAfterList();
   %nextIdx = $OptAfterN > 0 ? $OptAfter[0] : -1;
   if (%nextIdx >= 0)
   {
      %r = $OptSiteRate[%nextIdx];
      $OptThenPos = OptSitePos($OptSite[%nextIdx]);
      %t = %t @ $Opt::Grey @ "Then: " @ OptSiteName($OptSite[%nextIdx]) @ " (@THEN@) " @ OptRateColor(%r) @ %r @ $Opt::White @ "\n";
   }
   else
   {
      $OptThenPos = "";
   }

   // food under 3 days: a flashing line on top, the step above it stays
   %t = OptFoodWarn() @ %t;

   // one status line
   %net = OptFoodNet();
   %t = %t @ "Lv " @ OptShown(MainCharacter.Level) @ " (raw " @ MainCharacter.Level @ ") · A" @ $OptA["P", %L] @ " D" @ $OptD["P", %L] @ " · HP " @ mRound($OptHP["P", %L]) @ "/" @ mRound($OptMHP["P", %L]) @ " · Potions " @ OptPotions() @ " · Food " @ mFloor(gTown.Food) @ " (" @ (%net >= 0 ? "+" : "") @ OptNum(%net) @ "/day) · Fame " @ mFloor(gTown.Fame) @ " · " @ $OptSiteN @ " sites left\n";
   %t = %t @ OptGearLine();
   %t = %t @ OptTownLine();

   // only urgent warnings
   if (isObject(RequestFromKing) && !OptVis(FulFillRequestDialog) && OptNeedDisrespectful())
   {
      %pen = RequestFromKing.mydata.requestFamePenalty;
      %after = mFloor(gTown.Fame) - %pen;
      if (%after < 0) { %t = %t @ $Opt::Red @ "Request runs out in " @ RequestFromKing.timeRemaining @ " days: get " @ (-%after) @ " more fame first (kill +1, site +5)" @ $Opt::White @ "\n"; }
      else { %t = %t @ $Opt::Orange @ "Request: ignore it, Refuse when it runs out in " @ RequestFromKing.timeRemaining @ " days (-" @ %pen @ " fame)" @ $Opt::White @ "\n"; }
   }
   if ($OptRaidInfo !$= "") { %t = %t @ $Opt::Red @ $OptRaidInfo @ $Opt::White @ "\n"; }
   %room = OptTownRoom();
   if (%room !$= "" && getWord(%room, 0) < 1 && !OptLordInTown()) { %t = %t @ $Opt::Orange @ "Town inventory full: " @ getWords(%room, 1) @ "'s next items are lost until you make room in town" @ $Opt::White @ "\n"; }
   // loot that waits while your bag is full: how much, and where the nearest is
   if ($OptLootReady && OptFreeSlots() < 1)
   {
      %wn = 0;
      %wb = -1;
      for (%i = 0; %i < $OptLootN; %i++)
      {
         if (!isObject($OptLootObj[%i])) { continue; }
         %wn++;
         if (%wb < 0 || $OptLootDist[%i] < $OptLootDist[%wb]) { %wb = %i; }
      }
      if (%wn > 0) { %t = %t @ $Opt::Grey @ "Bag full: " @ %wn @ " bod" @ (%wn == 1 ? "y or chest waits" : "ies and chests wait") @ ", nearest " @ OptWhere($OptLootPos[%wb]) @ " · they never expire · F8 lists them" @ $Opt::White @ "\n"; }
   }
   if (gTown.Fame < 0) { %t = %t @ $Opt::Red @ "Fame below 0: " @ OptNum((MainCharacter.LossFromFameTime - MainCharacter.DaysInNegativeFame) / $gDayApparentCycle) @ " days to fix it (kill +1, site +5)" @ $Opt::White @ "\n"; }
   if (%net < 0 && gTown.Food / (-%net) >= 3 && gTown.Food / (-%net) < 8) { %t = %t @ $Opt::Orange @ "Food: " @ OptNum(gTown.Food / (-%net)) @ " days left" @ (OptFoodNetPlanned() >= 0 ? ", farm upgrade fixes it" : ", hire the next Farmer visitor") @ $Opt::White @ "\n"; }
   %t = %t @ OptDavidLine();
   if (%david && $OptTopIdx >= 0 && MainCharacter.Level <= $OptTopRaw - 20 && MainCharacter.Level >= $OptTopRaw - 22) { %t = %t @ $Opt::Orange @ "David window: " @ ($OptTopRaw - 20 - MainCharacter.Level) @ " raw levels left" @ $Opt::White @ "\n"; }
   if ($OptSiteN > 1 && $OptSiteN <= 5 && OptMissing() !$= "") { %t = %t @ $Opt::Orange @ "Leave one site for last. Still need: " @ OptMissing() @ $Opt::White @ "\n"; }

   if ($pref::Opt::Mode != 1)
   {
      %t = %t @ $Opt::Grey @ "F8: details" @ $Opt::White @ "\n";
      $OptBody = %t;
      return;
   }

   // details
   %shown = 0;
   for (%a = 1; %a < $OptAfterN && %shown < 4; %a++)
   {
      %i = $OptAfter[%a];
      if (%shown == 0) { %t = %t @ $Opt::Gold @ "AFTER THAT" @ $Opt::White @ "\n"; }
      %r = $OptSiteRate[%i];
      %t = %t @ "  " @ OptSiteName($OptSite[%i]) @ " (" @ OptWhere(OptSitePos($OptSite[%i])) @ ") " @ OptRateColor(%r) @ %r @ $Opt::White @ ($OptSiteExact[%i] ? "" : "*") @ ((%david && %i == $OptTopIdx) ? " · David target" : "") @ "\n";
      %shown++;
   }
   if (%shown > 0) { %t = %t @ $Opt::Grey @ "  * = estimate until you get close" @ $Opt::White @ "\n"; }
   %t = %t @ OptLootText();
   %t = %t @ OptTownText();
   %t = %t @ OptDavidText();
   %t = %t @ OptAchText();
   if ($OptSiteN > 0) { %t = %t @ OptGearText(OptGearSitePick()); }
   %t = %t @ OptHireText();
   %t = %t @ $Opt::Grey @ "F8: hide" @ $Opt::White @ "\n";
   $OptBody = %t;
}

// ------------------------------------------------------------------ new game screen

function OptPrefMark(%ok)
{
   return %ok ? $Opt::Green @ "ok" @ $Opt::White : $Opt::Red @ "change" @ $Opt::White;
}

function OptSetupNow()
{
   %n = 0;
   for (%i = 0; %i < getWordCount($Opt::AchIds); %i++)
   {
      eval("%id = " @ OptAchId(getWord($Opt::AchIds, %i)) @ ";");
      if (OptNeed(%id)) { %n++; }
   }
   if (%n == 0) { return "NEW GAME: you own all 9 achievements"; }
   return "NEW GAME: set these for the " @ %n @ " achievements you still need";
}

function OptSetupText()
{
   %needRun = OptNeedRun();
   %needDavid = OptNeed($Achievement_David);
   %wantLong = %needRun || OptNeed($Achievement_Champion);
   %wantHard = %needRun || %needDavid;
   %wantRaiders = %needRun || %needDavid;
   %wantRequests = OptNeed($Achievement_Disrespectful);
   %lone = OptNeed($Achievement_Lone_Wolf);
   %guard = OptNeed($Achievement_Guardian);
   %len = $pref::Player::GameLength;
   %dif = $pref::Player::GameDifficulty;
   %lenName = getWord("Short Medium Long", %len);
   %difName = getWord("Easy Normal Hard Hardcore", %dif);
   %t = "";
   %owned = OptOwnedList();
   if (%owned !$= "") { %t = %t @ $Opt::Green @ "  Already owned: " @ %owned @ $Opt::White @ "\n"; }
   if (%wantLong) { %t = %t @ "  Game length: Long (now " @ %lenName @ ") " @ OptPrefMark(%len >= 2) @ "\n"; }
   else { %t = %t @ "  Game length: Short is fastest (now " @ %lenName @ ") " @ OptPrefMark(1) @ "\n"; }
   if (%wantHard) { %t = %t @ "  Difficulty: Hard (now " @ %difName @ ") " @ OptPrefMark(%dif >= 2) @ "\n"; }
   else { %t = %t @ "  Difficulty: Easy gives the most XP and weakest enemies (now " @ %difName @ ") " @ OptPrefMark(1) @ "\n"; }
   if (%wantRaiders) { %t = %t @ "  Raiders: On " @ OptPrefMark($pref::player::raidersEnabled) @ "\n"; }
   else { %t = %t @ "  Raiders: Off (nothing left needs them) " @ OptPrefMark(!$pref::player::raidersEnabled) @ "\n"; }
   if (%wantRequests) { %t = %t @ "  Requests: On " @ OptPrefMark($pref::player::requestsEnabled) @ "\n"; }
   else { %t = %t @ "  Requests: Off (Disrespectful is done, refusing would only cost fame) " @ OptPrefMark(!$pref::player::requestsEnabled) @ "\n"; }
   %t = %t @ "  Region revealed: On (easier, no effect on achievements) " @ OptPrefMark($pref::Player::RevealedWorld) @ "\n";
   %t = %t @ "  All resources: On (easier, no effect on achievements) " @ OptPrefMark($pref::Player::AllResources) @ "\n";
   %cls = SetAllCharacterClasses.getObject($gStartingCharacterClass);
   %t = %t @ "  Class: Knight (now " @ getStringTableEntry(%cls.nameID) @ ") " @ OptPrefMark(%cls.getName() $= "dbCharClass_Knight") @ "\n";
   %t = %t @ $Opt::Gold @ "RULES IN GAME" @ $Opt::White @ "\n";
   if (%lone) { %t = %t @ "  Never press Join Party (Lone Wolf)\n"; }
   if (%guard) { %t = %t @ "  Hire what the town plan asks for; each one's window must say \"I'll avoid the enemy if the town is attacked\" (Guardian: nobody may die)\n"; }
   %t = %t @ "  First hires: a Farmer (then Manor + Large Farm for food), a Goodman (gold), a Tinker\n";
   if (%wantRequests) { %t = %t @ "  Never press Fulfill; Refuse requests only when they run out (Disrespectful)\n"; }
   if (%needDavid) { %t = %t @ "  Keep a bow for David\n"; }
   %t = %t @ "  Take Gatherer at the first level-up: +1 food/day feeds you\n";
   %t = %t @ $Opt::Grey @ "  The guide shows one step at a time in game. F8 switches step / details / hidden." @ $Opt::White @ "\n";
   return %t;
}
