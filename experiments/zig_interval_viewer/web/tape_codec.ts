// Binary tape format (matches src/main.zig `tape_upload`).
//
// Everything little-endian.
//
//   u32 version                  (= 1)
//   u32 op_count
//   u32 const_count
//   u32 output_slot
//   f32 constants[const_count]
//   { u32 op, u32 a, u32 b } ops[op_count]          // 12 bytes each
//
// The op numbers match the discriminant order of Zig's `tape.Op` enum.

export const TAPE_VERSION = 1;

export enum Op {
  InputX = 0,
  InputY = 1,
  InputZ = 2,
  Constant = 3,
  Neg = 4,
  Abs = 5,
  Sqrt = 6,
  Square = 7,
  Add = 8,
  Sub = 9,
  Mul = 10,
  Div = 11,
  Min = 12,
  Max = 13,
  Atan2 = 14,
}

export type Instruction = { op: Op; a: number; b: number };

export type Tape = {
  ops: Instruction[];
  constants: number[];
  outputSlot: number;
};

export function encodeTape(t: Tape): ArrayBuffer {
  const headerBytes = 16;
  const constBytes = t.constants.length * 4;
  const opBytes = t.ops.length * 12;
  const buf = new ArrayBuffer(headerBytes + constBytes + opBytes);
  const dv = new DataView(buf);

  dv.setUint32(0, TAPE_VERSION, true);
  dv.setUint32(4, t.ops.length, true);
  dv.setUint32(8, t.constants.length, true);
  dv.setUint32(12, t.outputSlot, true);

  let off = headerBytes;
  for (const c of t.constants) { dv.setFloat32(off, c, true); off += 4; }
  for (const ins of t.ops) {
    dv.setUint32(off + 0, ins.op, true);
    dv.setUint32(off + 4, ins.a, true);
    dv.setUint32(off + 8, ins.b, true);
    off += 12;
  }
  return buf;
}

// Small helper for building tapes from TypeScript. Useful for tests /
// procedural scenes; in production the F# host lowers FieldIR directly
// into the same binary format without going through this builder.
export class TapeBuilder {
  private ops: Instruction[] = [];
  private constants: number[] = [];

  x(): number { this.ops.push({ op: Op.InputX, a: 0, b: 0 }); return this.ops.length - 1; }
  y(): number { this.ops.push({ op: Op.InputY, a: 0, b: 0 }); return this.ops.length - 1; }
  z(): number { this.ops.push({ op: Op.InputZ, a: 0, b: 0 }); return this.ops.length - 1; }
  constant(v: number): number {
    const ci = this.constants.length;
    this.constants.push(v);
    this.ops.push({ op: Op.Constant, a: ci, b: 0 });
    return this.ops.length - 1;
  }
  private push1(op: Op, a: number): number { this.ops.push({ op, a, b: 0 }); return this.ops.length - 1; }
  private push2(op: Op, a: number, b: number): number { this.ops.push({ op, a, b }); return this.ops.length - 1; }
  neg(a: number) { return this.push1(Op.Neg, a); }
  abs(a: number) { return this.push1(Op.Abs, a); }
  sqrt(a: number) { return this.push1(Op.Sqrt, a); }
  square(a: number) { return this.push1(Op.Square, a); }
  add(a: number, b: number) { return this.push2(Op.Add, a, b); }
  sub(a: number, b: number) { return this.push2(Op.Sub, a, b); }
  mul(a: number, b: number) { return this.push2(Op.Mul, a, b); }
  div(a: number, b: number) { return this.push2(Op.Div, a, b); }
  min(a: number, b: number) { return this.push2(Op.Min, a, b); }
  max(a: number, b: number) { return this.push2(Op.Max, a, b); }
  atan2(y: number, x: number) { return this.push2(Op.Atan2, y, x); }

  finish(output: number): Tape {
    return { ops: this.ops.slice(), constants: this.constants.slice(), outputSlot: output };
  }
}
