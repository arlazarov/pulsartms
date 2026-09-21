// What the map asks of a deck.gl layer. The vendor is imported in one place
// only - gpuScene - so everything else says what it needs of a layer here,
// and is checked against that rather than against the vendor's own types.
export type DeckLayer = { props: Record<string, any> };

export type DeckLayerFactory = new (props: Record<string, any>) => DeckLayer;
