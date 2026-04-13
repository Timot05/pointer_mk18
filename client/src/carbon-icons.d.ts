declare module "@carbon/icons/es/*" {
  interface CarbonIconDescriptor {
    elem: string;
    attrs: Record<string, string | number>;
    content: CarbonIconDescriptor[];
  }
  const icon: CarbonIconDescriptor;
  export default icon;
}
