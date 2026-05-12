namespace Server

// ---------------------------------------------------------------------------
// SlotTable — a dense float array with (ActionId, Path) → slot index lookup.
//
// Every user-editable numeric value gets a slot. The viewer shader reads from
// these slots. When the user drags a value, only the slot's float is updated;
// no topology or shader rebuild.
// ---------------------------------------------------------------------------

open System.Collections.Generic

type Slot = int

type SlotRef = { ActionId: ActionId; Path: string }

/// 2D slot point — used by sketch primitives in Pickable / BlockCompile.
type SlotPt2 = { XSlot: Slot; YSlot: Slot }

type SlotTable =
    { Values: float array
      Index: Map<SlotRef, Slot> }

module SlotTable =

    /// Mutable builder used while walking the action graph during compile.
    type Builder =
        { Values: ResizeArray<float>
          Index: Dictionary<SlotRef, Slot> }

    let createBuilder () : Builder =
        { Values = ResizeArray()
          Index = Dictionary() }

    /// Returns the slot for this ref; allocates if new, reuses if already seen
    /// (idempotent). On first allocation seeds Values with the provided default.
    let alloc (b: Builder) (ref: SlotRef) (defaultVal: float) : Slot =
        match b.Index.TryGetValue(ref) with
        | true, slot -> slot
        | false, _ ->
            let slot = b.Values.Count
            b.Values.Add(defaultVal)
            b.Index.[ref] <- slot
            slot

    let tryGet (b: Builder) (ref: SlotRef) : Slot option =
        match b.Index.TryGetValue(ref) with
        | true, slot -> Some slot
        | false, _ -> None

    let toTable (b: Builder) : SlotTable =
        let index = b.Index |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq
        { Values = b.Values.ToArray(); Index = index }

    let tryFindSlot (table: SlotTable) (ref: SlotRef) : Slot option =
        Map.tryFind ref table.Index

    let patchedValues (values: float array) (updates: (Slot * float) list) : float array =
        let next = Array.copy values
        updates |> List.iter (fun (slot, value) -> next.[slot] <- value)
        next

    /// In-place update of a slot value. Returns true on hit, false if the
    /// ref isn't allocated. Used by the rapid-drag fast path.
    let update (table: SlotTable) (ref: SlotRef) (value: float) : bool =
        match Map.tryFind ref table.Index with
        | Some slot ->
            table.Values.[slot] <- value
            true
        | None -> false

    let valueAt (table: SlotTable) (ref: SlotRef) : float option =
        Map.tryFind ref table.Index
        |> Option.map (fun s -> table.Values.[s])
