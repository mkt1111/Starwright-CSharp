// Regenerates the C# port's embedded part catalog and stock-craft designs from the
// JavaScript sources. Run from the repository root: node tools/export-catalog.mjs
import { writeFileSync } from 'fs';
import { PARTS, CATEGORIES, G0, SIZES, RENAMED_PARTS, INTERSTAGE } from '../js/core/parts.js';
import { stockCrafts } from '../js/core/craft.js';

const parts = {};
for (const id of Object.keys(PARTS)) parts[id] = PARTS[id];
writeFileSync('godot/src/Starwright.Core/Data/parts.json', JSON.stringify({
  g0: G0, sizes: SIZES, categories: CATEGORIES, renamed: RENAMED_PARTS, interstage: INTERSTAGE, parts,
}));
const crafts = stockCrafts().filter(Boolean).map(c => c.toJSON());
writeFileSync('godot/src/Starwright.Core/Data/stock-crafts.json', JSON.stringify(crafts));
console.log(`wrote ${Object.keys(parts).length} parts and ${crafts.length} stock crafts`);
