# Licensing — the two editions

The macro is sold in two editions.  The **free** edition draws the dimension
chains; the **Pro** edition draws everything, and it is what the licence key
unlocks.

| | Free | Pro |
|---|---|---|
| the stud chain of every wall panel | yes | yes |
| the band chains of the bottom / left / right edge | yes | yes |
| the top outline of the layout | — | yes |
| the overall height and width | — | yes |
| the north run | — | yes |
| the 11 wall-panel marks | — | yes |
| the 5 section marks | — | yes |
| the 3 text notes | — | yes |

On the model of this project that is **14 dimension sets** in the free
edition and **37 objects** in the Pro edition (18 dimension sets, 11 marks,
5 section marks, 3 notes).

## How the edition is decided

At every run the macro asks the licence engine which edition this computer
may use:

```
%LOCALAPPDATA%\P142\WallFraming\license.txt   the key the customer was given
%LOCALAPPDATA%\P142\WallFraming\state.txt     the trial stamps and the state
HKCU\Software\P142\WallFraming                 a copy of the state, in the registry
```

The answer is one of:

| state | what the macro does |
|---|---|
| a valid key | Pro |
| no key, the trial is running | Pro |
| no key, the trial is over | Free |
| a key that does not verify | Free, and the message says why |
| a key that was issued for another computer | Free, and the message says why |
| a key whose date has passed | Free, and the message says when it ran out |

The **trial is three days** and it starts at the **first run** of the macro
on that computer — not at the first run after the key is pasted in.

## The trial

* three days, counted in **UTC**, from the first run;
* it is tied to the computer, like a licence;
* it is **voided** when the clock of the computer is moved back by more than
  an hour (one hour of difference is tolerated, so that a daylight-saving
  change or a slow clock does not end it).

The stamps are written to `state.txt` and to the registry, so deleting the
file alone does not give a new trial: the registry copy is read as well.

## The licence key

A key is one line of text:

```
WF1.<payload>.<signature>
```

The **payload** carries the customer, the expiry date, the machine ids and a
note.  The **signature** is RSA-2048 over SHA-256 of the payload, made with
the owner's **private** key.  The private key is not in the repository and
never will be — without it, nobody can produce a key that verifies.

The customer cannot change the key: any change to the payload breaks the
signature, and the macro then refuses it and says that the key is not valid.

### The machine binding

A key can be tied to one computer.  The computer is recognised by three
hashes, taken from:

| id | where it comes from |
|---|---|
| the machine GUID | `HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid` |
| the volume serial | `kernel32!GetVolumeInformation` |
| the processor id | WMI `Win32_Processor.ProcessorId` |

The raw values are never stored — they are hashed with SHA-256 first.

A key that carries machine ids is accepted when **at least two of the
three** match the computer it is used on.  One of the three can change
without the licence breaking: a disk is replaced, the machine is re-imaged,
the processor is upgraded.  A key that carries no ids at all works on every
computer.

### The activation step — **the owner has to do this once**

`source/WF_License.cs` holds the **public** key of the owner in its
constant `PublicKeyXml`.  Out of the box that constant is the placeholder

```csharp
public const string PublicKeyXml = "REPLACE_WITH_THE_PUBLIC_KEY";
```

and **nothing verifies** until the owner puts the real public key there and
rebuilds.  The steps are:

**Command Prompt (Windows)**

```
cd macros\wall-framing\source
powershell -NoProfile -File build.ps1 -Package
```

and before that:

```
WF_KeyGen.exe newkey  owner-private.xml  owner-public.xml
```

then copy the contents of `owner-public.xml` into `PublicKeyXml` in
`source/WF_License.cs` and rebuild.

> The **private** key is the owner's alone.  It is never written by an
> assistant, never committed, and never sent anywhere.

## The owner's tool — `WF_KeyGen.exe`

It is built with the rest (`source\bin\WF_KeyGen.exe`).  Every command is
run from a Command Prompt.

| command | what it does |
|---|---|
| `newkey <private.xml> <public.xml>` | makes a new key pair.  **Keep the private file safe.** |
| `machine` | prints the three ids of **this** computer, for use with `-d` |
| `issue <private.xml> -c "Customer" [-e 2027-12-31] [-d id,id,id] [-m thispc] [-p "note"]` | issues a key.  Without `-e` the key never expires.  `-m thispc` ties it to the computer the tool runs on. |
| `sign <private.xml> -k "<key>" -p "note"` | re-signs an existing key with a different note |
| `install "<key>"` | puts a key into **this** computer and takes it into use at once |
| `status` | the edition, the customer, the expiry and the trial of this computer |
| `remove` | takes the key away again; the trial applies once more |

### The usual work for a customer

1. The customer runs `WF_KeyGen.exe machine` and sends the three ids.
2. The owner issues the key:
   `WF_KeyGen.exe issue owner-private.xml -c "ACME" -e 2027-12-31 -d id,id,id`
3. The owner sends the key to the customer.
4. The customer puts the key in

   ```
   %LOCALAPPDATA%\P142\WallFraming\license.txt
   ```

   as the only content of the file, and runs the macro.  The macro takes the
   key into use on the first run after that.

   Or the owner does it on the customer's computer with
   `WF_KeyGen.exe install "<key>"`.

## What the free edition does *not* have

The Pro stages are **not in this repository**.  They are a separate
assembly, `WF_Pro.dll`, which is loaded at run time and only when the
licence allows it:

```csharp
if (edition == 1) RunPro(result);
```

The free edition is complete on its own: it finds the drawing, finds the
plan view, reads the model, groups the panels and writes the chains.  A
Pro licence on a computer that has no `WF_Pro.dll` still runs — it simply
does not get the Pro annotations, and the log says so.

## The file layout of an installation

```
<Tekla>\Environments\common\macros\drawings\
    P142_WF_AutoDimension.cs     the LOADER - the macro on the ribbon
    P142_WF_AutoDimension.bmp     its icon

%LOCALAPPDATA%\P142\WallFraming\
    WF_Core.dll                  the shared machinery
    WF_Rules.dll                 the free rules
    WF_License.dll               the licence engine
    WF_Pro.dll                   the Pro stages (only in the Pro package)
    license.txt                  the key of the customer
    state.txt                    the trial stamps
```

The loader holds **no rule and no drawing logic**: it is a thin file that
loads `WF_Core.dll` and hands the work over.  That is what keeps the Pro
stages out of the published repository, and it is why the installation is
two files in the macro catalog and a few modules beside them.
