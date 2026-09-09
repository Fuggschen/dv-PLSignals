## You ever played SimRail? You live in Poland? You just want different Signals? Then this Pack is for you!

### I present you today the Polish Signal Pack!

This Signal pack is as accurate as it gets while still considering the gameplay aspect of Derail Valley.

> It is recommended to play with "Enable Special Matching Path" ON in the "DV Signals" mod settings, but OFF is also supported.
> The Setting "Require Reserving Signals to Clear" can be ON or OFF, depending on your playstyle. ON will require you to reserve every signal you want to drive through.

### Installation instructions
Just install this zip via UMM or [DVMM](https://www.nexusmods.com/derailvalley/mods/1637) and select "PLSignals" as your Signal Pack in "DV Signals" mod settings.

### Requirements
* [DV Signals](https://www.nexusmods.com/derailvalley/mods/1636)

#### *If you like reading and want to get a more deepdive of the logic, continue below:*

This Signal Packs differentiates between Main Signals:
* Road Signal
* Entry Signal
* Exit Signal

Also between Distant Signals:
* Distant Signal
* Repeater Signal

And also between Shunting Signals:
* Shunting Signal
* Major Shunting Signal

How a signal behaves, depends entirely on what type of signal you are approaching, but the basic rules still apply (The possible signal aspect names will be shown in parentheses):

---

### Basic Rules:
Top 3 Lights of any Main Signal functions mostly like a traffic light:<br>
**Green**: Go as fast as the track allows (can be S2, S6, S10 or S10a)<br>
**Yellow**: Expect Stop on the next signal (can be S5, S9, S13 or S13a)<br>
**Red**: Stop (can be S1 or Sz)

> The first two lights can also flash:<br>
> **Green Flashing**: The next signal points to a fast (60km/h or above) junction path (can be S3, S7, S11 or S11a)<br>
> **Yellow Flashing**: The next signal points to a normal (40-50km/h) or slow (10-30km/h) junction path (can be S4, S8, S12 or S12a)

---

### Diverging Light:
The **bottom yellow light** (4th from top) only activates if the junction you are approaching is in a diverging position. Do with this information what you want since its not entirely consistent in Derail Valley (if its right or left) but usually points to the slower path.

---

### Speed Bar:
Then there is the Speed Bar (3 lights horizontal aligned below the main lights). Here it shows you the speed for **THIS** signal and **ONLY** if the path is **DIVERGING** (bottom yellow light is lit):<br>
**Green Bar**: 60km/h or above (can be S6, S7, S8 or S9)<br>
**Yellow Bar**: 40-50km/h (can be S10a, S11a, S12a or S13a)<br>
**No Bar**: 10-30km/h (can be S10, S11, S12 or S13)

The speeds are reduced from the original system to make it a better fit into the Derail Valley Network.

> Note to Road Signals:<br>
> If you encounter a Red Road Signal and want to pass it, use the Comms Radio to Reserve it. It will align the switches for you. If it then is still **Red** but the **White** Light blinks (called Sz), that means that this track is actually **occupied and you enter at your own risk!**

---

### Entry and Exit Signals:
Those have the addition "m" to them which means that they can show a shunting aspect.

> You can get a shunting clearance by reserving an Entry, Exit or Shunting Signal with the Comms Radio!

If Entry/Exit Signals point to any track that is occupied, they will show **Red**. Once you've got your shunting clearance, they will turn **White**, allowing you to proceed *ON SIGHT.*
> Damage caused during shunting cannot be blamed on a signal fault. You have been warned.

---

### Shunting Signals:
Those have a **Blue** and a White light.

**Blue**: Stop (Ms1)<br>
**White**: Proceed ON SIGHT (Ms2)

> This Pack uses custom reserving logic for Shunting Signals. If you Reserve an Entry or Exit signal, it will reserve also all shunting signals on it's path until it hits an occupied track or the next Main Signal. To allow entry to an occupied track, you need to reserve the **Blue** showing Shunting Signal at the occupied track again.

*Major Shunting Signals work the same way as normal shunting signals but they default to **Blue** and need reservation to clear, no matter what setting you choose in the "DV Signals" mod settings.*

---

### Distant and Repeater Signals:
Distant Signals are placed 300 meters before a main signal, if the track allows it.
Repeater Signals are placed 100 meters before a signal, if the main signal is visually obstructed or hard to see.

The meaning of the Lights is fairly simple:<br>
**Green**: Next Signal is Vmax (Os2/Sp2)<br>
**Green Blinking**: Next Signal is 60km/h or above (Os3/Sp3)<br>
**Yellow Blinking**: Next Signal is 10-50km/h (Os4/Sp4)<br>
**Yellow**: Next Signal is **Stop** (Os1/Sp1)

> Vmax only means track speed limit. Blinking only occurs if the next junction path is diverging

Repeater Signals have the same logic but are marked with an additional White light. Also **Yellow** and **Green** Lamps switch position.

---

### Signs:
#### There are two types of Signs in this Signal Pack:

W5:<br>
This is a half-circle with a black border mounted on a pole. This sign marks the maximum range a shunting train may leave the station. Gameplaywise this usually marks the position where jobs would despawn if you travel past that point (there is a bit of a buffer behind it but this is the safest distance).

Z1:<br>
This is a black square with white circle in the middle where a black bar crosses horizontally. This marks the end of Track on a buffer stop. You cannot continue past it. If you do, you f'ed up.

---

### Shout outs

#### Big thanks to the following persons who made it possible to have it as accurate as it gets:

* Pioterenewicz - Who helped in making the Infographic and did alot of testing and finding quirks
* Istvan - Alot of valuable information and bug hunting
* Wiz - For the amazing Signals Framework and help while developing
* [Voynaroveech on Sketchfab](https://sketchfab.com/3d-models/railway-light-signals-19ec11684eae47eb96474634750b4398) - For the 3D Model
* Absolarix - New Textures and Model Edits
* B0SS/Adrii_95 - Allowed me to use the Magnets and Z1 Signs from the base pack