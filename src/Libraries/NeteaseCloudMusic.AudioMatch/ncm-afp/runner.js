'use strict'

console.info = (...args) => console.error(...args)

const { GenerateFP } = require('./afp.js')

const chunks = []

process.stdin.on('data', chunk => {
  chunks.push(chunk)
})

process.stdin.on('end', async () => {
  try {
    const bytes = Buffer.concat(chunks)
    if (bytes.length === 0 || bytes.length % 4 !== 0) {
      throw new Error(`Expected little-endian Float32 PCM bytes, got ${bytes.length} byte(s)`)
    }

    const samples = new Float32Array(
      bytes.buffer,
      bytes.byteOffset,
      bytes.byteLength / Float32Array.BYTES_PER_ELEMENT,
    )

    const fingerprint = await GenerateFP(Float32Array.from(samples))
    process.stdout.write(JSON.stringify({ ok: true, fingerprint }))
  } catch (err) {
    process.stdout.write(JSON.stringify({
      ok: false,
      error: err && err.stack ? err.stack : String(err),
    }))
    process.exitCode = 1
  }
})
