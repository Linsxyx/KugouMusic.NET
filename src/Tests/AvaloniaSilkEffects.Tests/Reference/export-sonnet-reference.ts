import { resolve, join } from 'node:path';

const expectedCommit = 'd5b8b24d5c873362f17bb372028afdbc30a4d2b2';
const foliaRoot = resolve(process.env.FOLIA_SONNET_ROOT ?? '/Users/xiong/ka/folia-major');
const output = resolve(process.argv[2] ?? join(import.meta.dir, '../Fixtures/sonnet-reference.json'));

const git = Bun.spawnSync(['git', '-C', foliaRoot, 'rev-parse', 'HEAD']);
if (git.exitCode !== 0) throw new Error(new TextDecoder().decode(git.stderr));
const actualCommit = new TextDecoder().decode(git.stdout).trim();
if (actualCommit !== expectedCommit) {
    throw new Error(`Folia reference mismatch: expected ${expectedCommit}, got ${actualCommit}`);
}

const sonnet = join(foliaRoot, 'src/components/visualizer/sonnet');
const programModule = await import(join(sonnet, 'sonnetProgram.ts'));
const motion = await import(join(sonnet, 'sonnetMotion.ts'));
const transitions = await import(join(sonnet, 'sonnetTransitions.ts'));
const random = await import(join(sonnet, 'sonnetRandom.ts'));
const credits = await import(join(sonnet, 'sonnetCredits.ts'));
const guides = await import(join(sonnet, 'sonnetGuides.ts'));
const spatial = await import(join(sonnet, 'sonnetSpatialMgGeometry.ts'));
const background = await import(join(sonnet, 'sonnetBackgroundMgVariants.ts'));
const decor = await import(join(sonnet, 'sonnetBackgroundDecor.ts'));
const fixed = await import(join(sonnet, 'sonnetFixedGeoVariants.ts'));
const typographyRoles = await import(join(sonnet, 'sonnetTypographyRoles.ts'));
const glyphLayout = await import(join(sonnet, 'sonnetGlyphLayout.ts'));
const flowLayouts = await import(join(sonnet, 'sonnetShotFlowLayouts.ts'));
const posterBlocks = await import(join(sonnet, 'sonnetPosterBlocksLayout.ts'));
const additionalMg = await import(join(sonnet, 'sonnetAdditionalShotMg.ts'));

const line = (fullText: string, startTime: number, endTime: number, extra: Record<string, unknown> = {}) => ({
    fullText,
    startTime,
    endTime,
    words: [{ text: fullText, startTime, endTime }],
    ...extra,
});

const lines = [
    line('薄明かりに 名前を置いて', 0, 1.8, { blockIndex: 0, songPart: 'verse' }),
    line('世界， 再见！', 2.1, 3.9, { blockIndex: 0, songPart: 'verse' }),
    line("It's time, time.", 4.2, 6.1, { blockIndex: 0, songPart: 'verse' }),
    line('CHORUS / 再一次靠近', 8.8, 10.6, { blockIndex: 1, songPart: 'chorus', isChorus: true }),
    line('巨大な文字が 開いて', 11, 12.8, { blockIndex: 1, songPart: 'chorus', isChorus: true }),
    line('間奏 / instrumental break', 16, 18, { blockIndex: 2, songPart: 'break' }),
    line('最后一段 慢慢退场', 20, 22, { blockIndex: 3, songPart: 'outro' }),
];
const program = programModule.compileSonnetProgram(lines, 'parity-fixture');
const shotKinds = programModule.SONNET_SHOT_KINDS as string[];
const progressSamples = [0, 0.08, 0.18, 0.42, 0.78, 0.91, 1];
const semanticSegment = (
    text: string,
    startTime: number,
    endTime: number,
    isWordLike = true,
    includeGraphemes = true,
) => {
    const chars = Array.from(text);
    return {
        text,
        startOffset: 0,
        endOffset: text.length,
        startTime,
        endTime,
        wordIndices: [],
        graphemes: includeGraphemes
            ? chars.map((char, index) => ({
                char,
                startTime: startTime + (endTime - startTime) * index / Math.max(1, chars.length),
                endTime: startTime + (endTime - startTime) * (index + 1) / Math.max(1, chars.length),
            }))
            : [],
        isWordLike,
    };
};

const roleCases = [
    [semanticSegment('明かり', 0, 0.8), semanticSegment('に', 0.8, 1, false), semanticSegment('あなたへ', 1, 3)],
    ['在', '漫长', '句子', '前部重点', '仍然', '不断', '延伸', '最终的核心词语']
        .map((text, index) => semanticSegment(text, index * 0.3, index * 0.3 + 0.6)),
    ['左侧重点', '继续', '铺陈', '靠近', '中央英雄文字', '越过', '远方', '再次', '右侧重点']
        .map((text, index) => semanticSegment(text, index * 0.25, index * 0.25 + (index === 4 ? 2.5 : 0.8))),
];

const verticalGlyphSegment = semanticSegment('あなた', 2, 3.2);
const verticalGlyphPlacement = {
    segmentIndex: 0, displayText: 'あ\nな\nた', role: 'hero', fontScale: 1,
    measuredWidth: 60, measuredHeight: 162, x: 120, y: 80, rotation: 0,
    enterX: 0, enterY: 90, vertical: true, layoutDirection: 'vertical', timingPhase: 0.5,
};
const fallbackGlyphSegment = semanticSegment('A😀界', 1.25, 4.25, true, false);
const fallbackGlyphPlacement = {
    segmentIndex: 0, displayText: 'A😀界', role: 'support', fontScale: 1,
    measuredWidth: 122, measuredHeight: 48, x: -40, y: 32, rotation: 0.27,
    enterX: -18, enterY: 24, vertical: false, layoutDirection: 'horizontal', timingPhase: 0.2,
};

const makeFlowBoxes = () => [
    { index: 0, isHero: false, isSemiHero: false, displayText: '薄明', fontScale: 1.2, measuredWidth: 96, measuredHeight: 48, vertical: false, layoutDirection: 'horizontal', rotation: 0, x: 0, y: 0, enterX: 0, enterY: 0 },
    { index: 1, isHero: false, isSemiHero: false, displayText: 'から', fontScale: 1.1, measuredWidth: 82, measuredHeight: 46, vertical: false, layoutDirection: 'horizontal', rotation: 0, x: 0, y: 0, enterX: 0, enterY: 0 },
    { index: 2, isHero: false, isSemiHero: true, displayText: '伸びる', fontScale: 2.2, measuredWidth: 154, measuredHeight: 78, vertical: false, layoutDirection: 'horizontal', rotation: 0, x: 0, y: 0, enterX: 0, enterY: 0 },
    { index: 3, isHero: true, isSemiHero: false, displayText: '名前', fontScale: 4.6, measuredWidth: 252, measuredHeight: 164, vertical: true, layoutDirection: 'vertical', rotation: 0, x: 0, y: 0, enterX: 0, enterY: 0 },
    { index: 4, isHero: false, isSemiHero: false, displayText: '置いて', fontScale: 1.25, measuredWidth: 118, measuredHeight: 50, vertical: false, layoutDirection: 'horizontal', rotation: 0, x: 0, y: 0, enterX: 0, enterY: 0 },
    { index: 5, isHero: false, isSemiHero: false, displayText: '世界へ', fontScale: 1.35, measuredWidth: 136, measuredHeight: 54, vertical: false, layoutDirection: 'horizontal', rotation: 0, x: 0, y: 0, enterX: 0, enterY: 0 },
    { index: 6, isHero: false, isSemiHero: false, displayText: '再见', fontScale: 1.2, measuredWidth: 92, measuredHeight: 48, vertical: false, layoutDirection: 'horizontal', rotation: 0, x: 0, y: 0, enterX: 0, enterY: 0 },
];

const buildFlowCase = (
    kind: 'quiet' | 'ribbon' | 'cross' | 'editorial' | 'collage',
    variant: number,
    width: number,
    height: number,
) => {
    const boxes = makeFlowBoxes();
    if (kind === 'collage') {
        boxes[1].rotation = Math.PI / 2;
        boxes[5].rotation = -Math.PI / 2;
    }
    const inputBoxes = structuredClone(boxes);
    const gaps = flowLayouts.resolveSonnetFlowGaps(48);
    const context = { boxes, heroIndex: 3, width, height, ...gaps };
    if (kind === 'quiet') flowLayouts.layoutQuietTableau(context, variant);
    else if (kind === 'ribbon') flowLayouts.layoutTrackingRibbon(context, variant);
    else if (kind === 'cross') flowLayouts.layoutCrossStack(context);
    else if (kind === 'editorial') flowLayouts.layoutEditorialColumn(context, variant, 5);
    else flowLayouts.layoutFragmentCollage(context, variant);
    return { kind, variant, secondaryHeroIndex: 5, width, height, gaps, inputBoxes, boxes };
};

const makePosterBoxes = () => makeFlowBoxes().map((box, index) => ({
    ...box,
    verticalDisplayText: ['薄\n明', 'か\nら', '伸\nび\nる', '名\n前', '置\nい\nて', '世\n界\nへ', '再\n见'][index],
    verticalMeasuredWidth: [58, 54, 72, 112, 60, 64, 56][index],
    verticalMeasuredHeight: [104, 96, 190, 310, 156, 174, 106][index],
    verticalFontScale: box.fontScale * 0.92,
}));

const buildPosterCase = (seed: number, width: number, height: number) => {
    const boxes = makePosterBoxes();
    const inputBoxes = structuredClone(boxes);
    const plan = posterBlocks.layoutSonnetPosterBlocks(boxes, width, height, 48, seed);
    return { seed, width, height, baseFontSize: 48, inputBoxes, plan };
};

const createDrawRecorder = () => {
    let path: Record<string, unknown>[] = [];
    let length = 0;
    let lastX = 0;
    let lastY = 0;
    let strokeIndex = 0;
    let fillIndex = 0;
    const commands: Record<string, unknown>[] = [];
    const distance = (x1: number, y1: number, x2: number, y2: number) => Math.hypot(x2 - x1, y2 - y1);
    const target: any = {
        moveTo(x: number, y: number) { path.push({ verb: 'moveTo', a: x, b: y }); lastX = x; lastY = y; return target; },
        lineTo(x: number, y: number) {
            const segmentLength = distance(lastX, lastY, x, y);
            path.push({ verb: 'lineTo', a: x, b: y, length: segmentLength, lastX, lastY });
            length += segmentLength; lastX = x; lastY = y; return target;
        },
        quadraticCurveTo(cx: number, cy: number, tx: number, ty: number) {
            const segmentLength = distance(lastX, lastY, cx, cy) + distance(cx, cy, tx, ty);
            path.push({ verb: 'quadraticCurveTo', a: cx, b: cy, c: tx, d: ty, length: segmentLength, lastX, lastY });
            length += segmentLength; lastX = tx; lastY = ty; return target;
        },
        bezierCurveTo(c1x: number, c1y: number, c2x: number, c2y: number, tx: number, ty: number) {
            const segmentLength = distance(lastX, lastY, c1x, c1y) + distance(c1x, c1y, c2x, c2y) + distance(c2x, c2y, tx, ty);
            path.push({ verb: 'bezierCurveTo', a: c1x, b: c1y, c: c2x, d: c2y, e: tx, f: ty, length: segmentLength, lastX, lastY });
            length += segmentLength; lastX = tx; lastY = ty; return target;
        },
        arc(cx: number, cy: number, radius: number, start: number, end: number, anticlockwise = false) {
            let difference = end - start;
            if (anticlockwise && difference > 0) difference -= Math.PI * 2;
            else if (!anticlockwise && difference < 0) difference += Math.PI * 2;
            const segmentLength = Math.abs(difference) * radius;
            path.push({ verb: 'arc', a: cx, b: cy, c: radius, d: start, e: end, f: difference, anticlockwise, length: segmentLength, lastX, lastY });
            length += segmentLength; lastX = cx + Math.cos(end) * radius; lastY = cy + Math.sin(end) * radius; return target;
        },
        circle(x: number, y: number, radius: number) {
            const segmentLength = Math.PI * 2 * radius;
            path.push({ verb: 'circle', a: x, b: y, c: radius, length: segmentLength, lastX, lastY });
            length += segmentLength; lastX = x + radius; lastY = y; return target;
        },
        rect(x: number, y: number, width: number, height: number) {
            const segmentLength = 2 * (Math.abs(width) + Math.abs(height));
            path.push({ verb: 'rectangle', a: x, b: y, c: width, d: height, length: segmentLength, lastX, lastY });
            length += segmentLength; lastX = x; lastY = y; return target;
        },
        stroke(options: { color: number; width: number; alpha: number }) { paint('stroke', options); return target; },
        fill(options: { color: number; alpha: number }) { paint('fill', { ...options, width: 0 }); return target; },
    };
    const paint = (kind: 'stroke' | 'fill', options: { color: number; width: number; alpha: number }) => {
        if (path.length === 0) return;
        const index = kind === 'stroke' ? strokeIndex++ : fillIndex++;
        const slot = (index * 0.6180339887498949) % 1;
        const jitter = ((index * 2654435761) >>> 0) / 4294967296;
        const delay = slot * (kind === 'stroke' ? 0.5 : 0.45);
        const span = kind === 'stroke' ? 0.32 + jitter * 0.26 : 0.4 + jitter * 0.25;
        commands.push({ kind, path, color: options.color >>> 0, alpha: options.alpha, width: options.width,
            length, staggerDelay: delay, staggerSpan: Math.min(span, 1 - delay) });
        path = []; length = 0;
    };
    return { target, commands };
};

const buildSpatialReference = () => {
    const recipes = [
        ['solid-cuboid', (target: any) => spatial.drawSonnetSolidCuboid(target, 12, -18, 160, 96, 34, -22, 0x8fd3ff, 0.72)],
        ['triangular-prism', (target: any) => spatial.drawSonnetTriangularPrism(target, -20, 14, 180, 120, 28, -19, 0xff6f91, 0.64)],
        ['hexagonal-prism', (target: any) => spatial.drawSonnetHexagonalPrism(target, 8, 4, 190, 130, -32, 24, 0xa8ff78, 0.58)],
        ['trapezoid-prism', (target: any) => spatial.drawSonnetTrapezoidPrism(target, 0, -6, 110, 210, 120, 30, 18, 0xf4d35e, 0.68)],
    ] as const;
    return recipes.map(([name, draw]) => { const recorder = createDrawRecorder(); draw(recorder.target); return { name, commands: recorder.commands }; });
};

const buildAdditionalMgReference = () => Array.from({ length: 82 }, (_, offset) => {
    const variant = offset + 18;
    const recorder = createDrawRecorder();
    const handled = additionalMg.drawAdditionalSonnetShotMg({
        target: recorder.target, variant, radius: 720, width: 1280, height: 720,
        seed: 0x12345678 + variant, primary: 0xe8f1ff, secondary: 0xff5d8f,
    });
    return { variant, handled, commands: recorder.commands };
});

const reference = {
    manifest: {
        schema: 1,
        foliaVersion: '0.7.2',
        commit: actualCommit,
        viewport: { width: 1280, height: 720, deviceScaleFactor: 1.5 },
        fontFamily: 'PingFang SC',
    },
    random: ['sonnet', 'parity-fixture', '世界， 再见！'].map(value => ({
        value,
        hash: random.hashSonnetSeed(value),
    })),
    program,
    motion: shotKinds.flatMap(kind => progressSamples.map(progress => ({
        kind,
        progress,
        pathProgress: motion.resolveShotPathProgress(kind, progress),
        frame: motion.resolveShotMotionFrame(kind, progress),
    }))),
    cameraBreath: [0, 1.25, 9.75].map(time => ({
        time,
        frame: motion.resolveSonnetCameraBreath(time, 0.37),
    })),
    focusWeights: [0.5, 1.5, 3, 4.5, 6].map(time => ({
        time,
        weights: motion.resolveSonnetFocusWeights([
            { startTime: 1, endTime: 2 },
            { startTime: 4, endTime: 5 },
        ], time),
    })),
    transitions: ['fast-blur', 'mono-glitch', 'camera-pull'].flatMap(kind =>
        [false, true].flatMap(entering => [0, 0.25, 0.5, 0.75, 1].map(progress => ({
            kind,
            entering,
            progress,
            frame: transitions.resolveSonnetTransitionEffectFrame(kind, entering ? 'enter' : 'exit', progress, 0x12345678),
        })))),
    credits: [-0.1, 0, 0.38, 0.9, 1.93, 3].map(elapsed => ({
        elapsed,
        frame: credits.resolveSonnetCreditsFrame(10 + elapsed, 10),
    })),
    guideCues: [0.1, 1, 10].map(duration => ({
        duration,
        cue: guides.resolveSonnetGuideCue({ startTime: 5, endTime: 5 + duration }),
    })),
    typographyRoles: {
        weights: [null, 99, 105, 520, 946].flatMap(weight =>
            ['hero', 'semi-hero', 'support', 'decoration'].map(role => ({
                weight,
                role,
                resolved: typographyRoles.resolveSonnetRoleFontWeight(weight, role),
            }))),
        cases: roleCases.map(segments => {
            const hero = typographyRoles.findSonnetHeroSegmentIndex(segments);
            return {
                segments,
                visibleLengths: segments.map(typographyRoles.getSonnetVisibleSegmentLength),
                scores: segments.map(typographyRoles.scoreSonnetHeroSegment),
                hero,
                semiHeroes: typographyRoles.findSonnetSemiHeroSegmentIndices(segments, hero),
            };
        }),
    },
    glyphLayouts: [
        {
            name: 'vertical-timed',
            segment: verticalGlyphSegment,
            placement: verticalGlyphPlacement,
            fontSize: 60,
            measures: { 'あ': 60, 'な': 60, 'た': 60 },
            window: { startTime: 2, endTime: 6 },
            duration: glyphLayout.resolveSonnetGlyphMotionDuration({ startTime: 2, endTime: 6 }),
            glyphs: glyphLayout.buildSonnetGlyphLayout(
                verticalGlyphSegment, verticalGlyphPlacement, 60, () => 60, { startTime: 2, endTime: 6 }),
        },
        {
            name: 'horizontal-fallback-unicode',
            segment: fallbackGlyphSegment,
            placement: fallbackGlyphPlacement,
            fontSize: 44,
            measures: { A: 26, '😀': 51, '界': 45 },
            window: { startTime: 0, endTime: 10 },
            duration: glyphLayout.resolveSonnetGlyphMotionDuration({ startTime: 0, endTime: 10 }),
            glyphs: glyphLayout.buildSonnetGlyphLayout(
                fallbackGlyphSegment, fallbackGlyphPlacement, 44,
                (char: string) => ({ A: 26, '😀': 51, '界': 45 })[char] ?? 20,
                { startTime: 0, endTime: 10 }),
        },
    ],
    flowLayouts: [
        ...[0, 1, 2, 3].map(variant => buildFlowCase('quiet', variant, 1280, 720)),
        buildFlowCase('quiet', 0, 560, 420),
        ...[0, 1, 2].map(variant => buildFlowCase('ribbon', variant, 1280, 720)),
        buildFlowCase('ribbon', 1, 720, 420),
        buildFlowCase('cross', 0, 1280, 720),
        buildFlowCase('cross', 0, 720, 420),
        ...[0, 1, 2, 3, 4].map(variant => buildFlowCase('editorial', variant, 1280, 720)),
        buildFlowCase('editorial', 1, 560, 420),
        ...[0, 1, 2].map(variant => buildFlowCase('collage', variant, 1280, 720)),
        buildFlowCase('collage', 2, 720, 420),
    ],
    posterLayouts: [
        buildPosterCase(0, 1280, 720),
        buildPosterCase(1, 1280, 720),
        buildPosterCase(2, 720, 420),
        buildPosterCase(3, 560, 360),
    ],
    drawLists: {
        spatialPrisms: buildSpatialReference(),
        additionalVariants: buildAdditionalMgReference(),
    },
    variants: Array.from({ length: 300 }, (_, seed) => ({
        seed,
        geo: spatial.resolveSonnetGeoVariant(seed),
        molecule: spatial.resolveSonnetMoleculeVariant(seed),
        hudRotation: spatial.resolveSonnetHudRotationQuarterTurns(seed),
        background: background.resolveSonnetBackgroundMgVariant(seed),
        decor: decor.resolveSonnetBackgroundDecorVariant(seed),
        fixed: fixed.resolveSonnetFixedGeoVariant(seed),
    })),
};

await Bun.write(output, `${JSON.stringify(reference, null, 2)}\n`);
console.log(output);
