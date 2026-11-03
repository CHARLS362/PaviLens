import { parseImageStream, recordsOfKind, decodeMaskToSVGPath } from '@meta-sam/parser';

// Only geometry enters this process. API credentials and images stay in the .NET client.
let input = '';
for await (const chunk of process.stdin) {
  input += chunk;
  if (input.length > 16 * 1024 * 1024) throw new Error('Segmentation response exceeds the decoder limit.');
}
try {
  const response = JSON.parse(input);
  if (response.status !== 'completed') throw new Error('SAM response is incomplete.');
  const parts = (response.output ?? []).flatMap(item => item.content ?? []);
  if (parts.some(part => part.type === 'refusal')) throw new Error('SAM refused this input.');
  const text = parts.filter(part => part.type === 'output_text').map(part => part.text).join('');
  if (!text.trim()) { process.stdout.write('{"objects":[]}'); }
  else {
    async function* events() {
      yield { type: 'response.output_text.done', item_id: 'sam-image', output_index: 0, content_index: 0, text };
      yield { type: 'response.completed', response };
    }
    const parsed = await parseImageStream(events()).finalResult;
    if (parsed.outcome.status !== 'completed' || parsed.diagnostics.length) throw new Error('Malformed or incomplete SAM output.');
    const objects = [];
    for (const record of recordsOfKind(parsed.records, 'mask')) {
      if (!Number.isFinite(record.confidence)) throw new Error('SAM did not return requested confidence values.');
      const { bounds, mask } = record;
      const path = decodeMaskToSVGPath(mask);
      const rings = [...path.matchAll(/M[^Z]*Z/g)].map(match => [...match[0].matchAll(/[ML](-?[\d.]+)[ ,]+(-?[\d.]+)/g)].map(point => [
        // The SDK contour uses pixel centers (-0.5 at the first outer edge).
        bounds.left + (Number(point[1]) + 0.5) / mask.width * (bounds.right - bounds.left),
        bounds.top + (Number(point[2]) + 0.5) / mask.height * (bounds.bottom - bounds.top),
      ]));
      const contains = (ring, p) => {
        let inside = false;
        for (let i = 0, j = ring.length - 1; i < ring.length; j = i++) {
          const a = ring[i], b = ring[j];
          if ((a[1] > p[1]) !== (b[1] > p[1]) && p[0] < (b[0]-a[0])*(p[1]-a[1])/(b[1]-a[1])+a[0]) inside = !inside;
        }
        return inside;
      };
      const holes = rings.some((r, i) => r.length && rings.some((other, j) => i !== j && contains(other, r[0])));
      objects.push({ confidence: record.confidence, rings, holes });
    }
    process.stdout.write(JSON.stringify({ objects }));
  }
} catch (error) {
  process.stderr.write(error instanceof Error ? error.message : 'SAM decoding failed.');
  process.exitCode = 1;
}
