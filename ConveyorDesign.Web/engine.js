// 컨베어 구동부 선정 엔진 — ConveyorDesign(C#) ConveyorCalculator 의 JS 이식판
(function (global) {
  'use strict';
  const G = 9.81;

  const D = {
    motorKw: [0.1, 0.2, 0.4, 0.75, 1.5, 2.2, 3.7, 5.5, 7.5, 11, 15, 18.5, 22, 30, 37, 45, 55],
    reducerRatios: [3, 5, 7.5, 10, 12.5, 15, 20, 25, 30, 40, 50, 60, 80, 100, 120, 150, 200, 240, 300, 360, 450, 600, 900, 1200],
    servoRatios: [1, 3, 4, 5, 7, 9, 10, 15, 20, 25, 30, 35, 40, 45, 50, 60, 70, 80, 100],
    servos: [[0.05, 0.025e-4], [0.1, 0.05e-4], [0.2, 0.22e-4], [0.4, 0.37e-4], [0.75, 1.3e-4], [1.0, 2.0e-4], [1.5, 3.0e-4], [2.0, 4.0e-4], [3.0, 6.0e-4], [5.0, 10e-4], [7.0, 15e-4], [11.0, 30e-4], [15.0, 45e-4]],
    teeth: [11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 28, 30, 32, 35, 38, 40, 45, 48, 50, 60],
    pulleys: [30, 40, 50, 60, 76, 89, 100, 114, 125, 139, 150, 165, 200, 250, 315, 400, 500, 630],
    rollers: [38.1, 42.7, 48.6, 50.8, 57, 60.5, 76.3, 89.1, 101.6],
    ratingRpm: [10, 25, 50, 100, 200, 300, 500, 700, 1000, 1400],
    chains: [
      ['RS25', 6.35, 3.5, 0.44, 0.15, [0.012, 0.03, 0.055, 0.10, 0.19, 0.27, 0.43, 0.57, 0.78, 1.0]],
      ['RS35', 9.525, 7.8, 1.07, 0.33, [0.03, 0.07, 0.13, 0.25, 0.47, 0.67, 1.07, 1.44, 1.95, 2.5]],
      ['RS40', 12.70, 17.7, 3.0, 0.64, [0.06, 0.14, 0.26, 0.49, 0.92, 1.32, 2.1, 2.85, 3.9, 5.1]],
      ['RS50', 15.875, 29.4, 5.1, 1.04, [0.12, 0.27, 0.50, 0.95, 1.78, 2.57, 4.1, 5.5, 7.5, 9.8]],
      ['RS60', 19.05, 44.1, 7.5, 1.53, [0.20, 0.46, 0.86, 1.63, 3.07, 4.4, 7.0, 9.5, 12.9, 16.5]],
      ['RS80', 25.40, 78.5, 13.6, 2.66, [0.46, 1.05, 1.95, 3.7, 6.9, 10.0, 15.9, 21.3, 28.5, 35.0]],
      ['RS100', 31.75, 118.0, 21.1, 3.99, [0.87, 2.0, 3.7, 7.0, 13.2, 19.0, 30.0, 40.0, 51.0, 58.0]],
      ['RS120', 38.10, 167.0, 30.0, 5.90, [1.5, 3.4, 6.3, 12.0, 22.5, 32.5, 51.0, 67.0, 80.0, 85.0]],
      ['RS140', 44.45, 226.0, 40.0, 7.84, [2.3, 5.3, 9.9, 18.8, 35.0, 50.5, 78.0, 100.0, 110.0, 110.0]],
      ['RS160', 50.80, 294.0, 53.0, 10.20, [3.4, 7.9, 14.7, 27.8, 52.0, 75.0, 113.0, 135.0, 140.0, 140.0]],
      ['RS200', 63.50, 461.0, 83.0, 16.30, [6.3, 14.5, 27.0, 51.0, 95.0, 135.0, 190.0, 210.0, 210.0, 210.0]],
    ].map(c => ({ no: c[0], pitch: c[1], tensile: c[2], allow: c[3], mass: c[4], kw: c[5] })),
  };

  const pcd = (p, z) => p / Math.sin(Math.PI / z);
  const ceilStd = (v, t) => { for (const x of t) if (x >= v - 1e-9) return x; return t[t.length - 1]; };
  const nearStd = (v, t) => t.slice().sort((a, b) => Math.abs(a - v) - Math.abs(b - v))[0];
  const motorRpm = (poles, hz) => { const s = 120 * hz / poles; const slip = { 2: 0.04, 4: 0.045, 6: 0.05 }[poles] ?? 0.06; return Math.round(s * (1 - slip)); };
  const sf = (lc, dh) => lc === 'uniform' ? 1 : lc === 'light' ? (dh === 'over10' ? 1.25 : 1) : (dh === 'under3' ? 1 : dh === 'h3to10' ? 1.25 : 1.5);
  const k1chain = lc => lc === 'uniform' ? 1.0 : lc === 'light' ? 1.3 : 1.5;
  const cFactor = (tr, starts) => tr === 'direct' ? (starts > 70 ? 1.5 : 1.0) : (starts > 70 ? 3.0 : 2.0);
  const k2ohl = p => p === 'root' ? 0.75 : p === 'end' ? 1.5 : 1.0;
  const chainByPitch = p => {
    let c = D.chains.find(c => Math.abs(c.pitch - p) < 0.05); if (c) return c;
    c = D.chains.find(c => Math.abs(c.pitch * 2 - p) < 0.05);
    if (c) return Object.assign({}, c, { no: c.no.replace('RS', 'C2') + '0(더블피치)', pitch: p, mass: c.mass * 0.8 });
    return null;
  };
  const chainRating = (c, rpm, z) => {
    if (rpm <= 0) return 0;
    const x = D.ratingRpm, y = c.kw; let kw;
    if (rpm <= x[0]) kw = y[0] * rpm / x[0];
    else if (rpm >= x[x.length - 1]) kw = y[y.length - 1];
    else { let i = 0; while (rpm > x[i + 1]) i++; kw = Math.exp(Math.log(y[i]) + (Math.log(y[i + 1]) - Math.log(y[i])) * (Math.log(rpm) - Math.log(x[i])) / (Math.log(x[i + 1]) - Math.log(x[i]))); }
    return kw * Math.pow(z / 17, 0.9);
  };
  const f = (v, d = 2) => (Math.round(v * 10 ** d) / 10 ** d).toLocaleString('ko-KR', { maximumFractionDigits: d });
  const pct = v => (v >= 0 ? '+' : '') + f(v, 1) + ' %';

  const names = {
    load: { uniform: '균일 부하', light: '가벼운 충격 부하', heavy: '무거운 충격 부하' },
    hours: { under3: '3시간 이하/일', h3to10: '3~10시간/일', over10: '10시간 이상/일' },
    ohl: { root: '축의 밑부분', center: '축의 중앙', end: '축단' },
  };

  function defaults() {
    return {
      projectName: '컨베어-01', type: 'belt', beltKind: 'rubber',
      speed: 20, length: 10, angle: 0, workMass: 20, workCount: 10, accumulation: false, frictionProduct: 0.3,
      beltWidth: 500, beltMassPerM2: 3, frictionBelt: 0.3, pulleyDiameter: 150, pulleyMass: 15, wrapAngle: 180, frictionPulley: 0.3, beltAllowableTension: 8,
      c1StartStop: false, c1Pushing: false, c2Temperature: 1.0, c3Max: 30,
      rollerDiameter: 60.5, rollerPitch: 100, rollerMass: 2.5, rollerShaftDiameter: 15, frictionRolling: 0.05, frictionBearing: 0.05, rollerChainMassPerM: 1.0,
      chainPitch: 38.1, chainStrands: 2, conveyorSprocketTeeth: 15, chainMassPerM: 4.0, frictionChain: 0.2, conveyorSprocketMass: 5.0, chainAllowableLoad: 0,
      motorKind: 'induction', motorPoles: 4, frequency: 60, motorMargin: 1.2,
      servoRatedRpm: 3000, servoMaxTorqueRatio: 3.0, servoMaxInertiaRatio: 10, servoInertiaAsWarning: false, servoTorqueUtilization: 0.8, servoSpeedUtilization: 0.9,
      moveDistance: 0, decelTime: 0.2, dwellTime: 0.5,
      transmission: 'chain', autoSelectSprockets: true, driveSprocketTeeth: 15, drivenSprocketTeeth: 30, manualReducerRatio: 0, transmissionCenterDistance: 500,
      reducerEfficiency: 0.9, transmissionEfficiency: 0.95,
      loadClass: 'light', dailyHours: 'over10', startsPerDay: 50, ohlPosition: 'center', accelTime: 1.0,
    };
  }

  function typeName(i) { return i.type === 'belt' ? (i.beltKind === 'modular' ? '벨트 컨베어 (모듈벨트)' : '벨트 컨베어') : i.type === 'roller' ? '롤러 컨베어' : '체인 컨베어'; }

  function validate(i) {
    const e = m => { throw new Error(m); };
    if (!(i.speed > 0)) e('반송속도는 0보다 커야 합니다.');
    if (!(i.length > 0)) e('컨베어 길이는 0보다 커야 합니다.');
    if (i.workCount < 0 || i.workMass < 0) e('워크 질량/개수는 음수일 수 없습니다.');
    if (Math.abs(i.angle) >= 90) e('경사각은 ±90° 미만이어야 합니다.');
    if (!(i.reducerEfficiency > 0 && i.reducerEfficiency <= 1)) e('감속기 효율은 0~1 사이여야 합니다.');
    if (!(i.accelTime > 0)) e('가속 시간은 0보다 커야 합니다.');
    if (i.motorKind === 'servo') {
      if (!(i.decelTime > 0)) e('감속 시간은 0보다 커야 합니다.');
      if (i.dwellTime < 0 || i.moveDistance < 0) e('정지 시간/이동거리는 음수일 수 없습니다.');
      if (!(i.servoRatedRpm > 0) || i.servoMaxTorqueRatio < 1 || !(i.servoMaxInertiaRatio > 0)) e('서보 조건(정격 회전수, 최대토크 배율, 허용 관성비)을 확인하십시오.');
      if (!(i.servoTorqueUtilization > 0 && i.servoTorqueUtilization <= 1) || !(i.servoSpeedUtilization > 0)) e('서보 토크/속도 사용률을 확인하십시오.');
    }
    if (i.type === 'belt') { if (!(i.pulleyDiameter > 0 && i.beltWidth > 0)) e('풀리 외경/벨트 폭을 확인하십시오.'); if (i.beltKind === 'modular' && !(i.c2Temperature > 0)) e('온도계수 C2는 0보다 커야 합니다.'); }
    if (i.type === 'roller' && !(i.rollerDiameter > 0 && i.rollerPitch > 0)) e('롤러 외경/피치를 확인하십시오.');
    if (i.type === 'chain' && (!(i.chainPitch > 0) || i.conveyorSprocketTeeth < 6 || i.chainStrands < 1)) e('체인 피치/스프라켓 잇수/열수를 확인하십시오.');
    if (i.transmission === 'chain') {
      if (!i.autoSelectSprockets && (i.driveSprocketTeeth < 6 || i.drivenSprocketTeeth < 6)) e('전동 스프라켓 잇수를 확인하십시오.');
      if (!(i.transmissionCenterDistance > 0)) e('전동 체인 축간거리를 확인하십시오.');
    }
  }

  // ───────────── 부하 ─────────────
  function beltLoad(i, r, L) {
    const m = i.workMass * i.workCount, mB = i.beltWidth / 1000 * 2 * i.length * i.beltMassPerM2;
    const cos = Math.cos(i.angle * Math.PI / 180), sin = Math.sin(i.angle * Math.PI / 180);
    const fFric = i.frictionBelt * G * (m + mB) * cos, fLift = G * m * sin, fAcc = i.accumulation ? i.frictionProduct * G * m : 0;
    const fu = fFric + fLift + fAcc, D1 = i.pulleyDiameter;
    L.push('[1] 부하 — 벨트 컨베어 (' + (i.beltKind === 'modular' ? '모듈 플라스틱 벨트, FMS 계산법' : '고무/PVC 벨트') + ')');
    L.push(`   벨트 질량 mB = b0 × 2L × 단위질량 = ${f(i.beltWidth / 1000, 3)} m × ${2 * i.length} m × ${i.beltMassPerM2} kg/m² = ${f(mB)} kg`);
    L.push(`   마찰 저항 = μT·g·(m+mB)·cosθ = ${i.frictionBelt}·9.81·(${f(m)}+${f(mB)})·${f(cos, 3)} = ${f(fFric, 1)} N`);
    L.push(`   경사 저항 = g·m·sinθ = 9.81·${f(m)}·${f(sin, 3)} = ${f(fLift, 1)} N`);
    if (i.accumulation) L.push(`   축적 저항 = μST·g·m = ${i.frictionProduct}·9.81·${f(m)} = ${f(fAcc, 1)} N`);
    L.push(`   유효장력 FU = ${f(fu, 1)} N`);
    r.add('load', '총 적재하중 m', f(m), 'kg');
    r.add('load', '벨트 질량 mB', f(mB), 'kg', '상·하 양측');
    r.add('load', '유효장력 FU', f(fu, 1), 'N', `마찰 ${f(fFric, 1)} + 경사 ${f(fLift, 1)}` + (i.accumulation ? ` + 축적 ${f(fAcc, 1)}` : ''));
    let fw;
    if (i.beltKind === 'modular') {
      const c1 = 1 + (i.c1StartStop ? 0.2 : 0) + (i.c1Pushing ? 0.2 : 0) + (i.speed > 30 ? 0.2 : 0) + (i.angle > 0 ? 0.4 : 0);
      const fb = fu * c1 / i.c2Temperature, c3 = fb / i.beltWidth; fw = fu * c1 + i.pulleyMass * G;
      L.push(`   C1 = 1.0${i.c1StartStop ? ' +0.2(시작-멈춤)' : ''}${i.c1Pushing ? ' +0.2(종동)' : ''}${i.speed > 30 ? ' +0.2(V>30)' : ''}${i.angle > 0 ? ' +0.4(상승)' : ''} = ${f(c1, 1)},  C2 = ${i.c2Temperature}`);
      L.push(`   벨트 선택장력 FB = FU·C1/C2 = ${f(fu, 1)}·${f(c1, 1)}/${i.c2Temperature} = ${f(fb, 1)} N`);
      L.push(`   C3 = FB / b0 = ${f(fb, 1)} / ${i.beltWidth} = ${f(c3)} N/mm ≤ C3max ${i.c3Max} → ${c3 <= i.c3Max ? 'OK' : 'NG'}`);
      L.push(`   샤프트 로드 FW ≈ FU·C1 + mW·g = ${f(fw, 1)} N`);
      r.add('load', '벨트 선택장력 FB', f(fb, 1), 'N', `C1=${f(c1, 1)}, C2=${i.c2Temperature}`);
      r.add('load', '폭당 장력 C3', f(c3), 'N/mm', `허용 ${i.c3Max} → ${c3 <= i.c3Max ? 'OK' : 'NG'} (${f(c3 / i.c3Max * 100, 0)}%)`, c3 <= i.c3Max ? 'ok' : 'ng');
      if (c3 > i.c3Max) r.warn('모듈벨트 폭당 장력 C3가 허용값을 초과합니다. 벨트 시리즈 변경 또는 폭 확대 필요.');
    } else {
      const th = i.wrapAngle * Math.PI / 180, k = Math.exp(i.frictionPulley * th);
      const t1 = fu * k / (k - 1), t2 = t1 - fu, tw = t1 / i.beltWidth; fw = t1 + t2;
      L.push(`   오일러식: e^(μθ) = e^(${i.frictionPulley}·${f(th, 3)}) = ${f(k, 3)}`);
      L.push(`   긴장측 T1 = FU·e^(μθ)/(e^(μθ)−1) = ${f(t1, 1)} N,  이완측 T2 = T1 − FU = ${f(t2, 1)} N`);
      L.push(`   폭당 장력 = T1 / b0 = ${f(t1, 1)} / ${i.beltWidth} = ${f(tw)} N/mm ≤ 허용 ${i.beltAllowableTension} → ${tw <= i.beltAllowableTension ? 'OK' : 'NG'}`);
      L.push(`   풀리 축하중 FW ≈ T1 + T2 = ${f(fw, 1)} N`);
      r.add('load', '긴장측 장력 T1', f(t1, 1), 'N');
      r.add('load', '이완측 장력 T2', f(t2, 1), 'N', 'T2 ≥ 0 이어야 미끄러짐 없음');
      r.add('load', '벨트 폭당 장력', f(tw), 'N/mm', `허용 ${i.beltAllowableTension} N/mm → ${tw <= i.beltAllowableTension ? 'OK' : 'NG'}`, tw <= i.beltAllowableTension ? 'ok' : 'ng');
      if (tw > i.beltAllowableTension) r.warn('벨트 폭당 장력이 허용값을 초과합니다. 벨트 강도 상향 또는 폭 확대 필요.');
    }
    r.add('load', '구동 풀리 외경 D1', f(D1, 1), 'mm');
    r.add('load', '구동 풀리 축하중 FW', f(fw, 1), 'N', '베어링 선정용');
    const rr = D1 / 2000;
    return { fu, D1, J: (m + mB) * rr * rr + i.pulleyMass * rr * rr, basis: `풀리 외경 ${D1} mm 기준`, rpmToV: rpm => rpm * Math.PI * D1 / 1000 };
  }

  function rollerLoad(i, r, L) {
    const m = i.workMass * i.workCount, nR = Math.floor(i.length * 1000 / i.rollerPitch) + 1, mR = nR * i.rollerMass, mC = i.rollerChainMassPerM * 2 * i.length;
    const cos = Math.cos(i.angle * Math.PI / 180), sin = Math.sin(i.angle * Math.PI / 180), D1 = i.rollerDiameter;
    const fRoll = i.frictionRolling * G * m * cos, fBear = i.frictionBearing * G * (m * cos + mR) * (i.rollerShaftDiameter / D1), fCh = 0.1 * G * mC * 0.5, fLift = G * m * sin, fAcc = i.accumulation ? i.frictionProduct * G * m : 0;
    const fu = fRoll + fBear + fCh + fLift + fAcc;
    L.push('[1] 부하 — 롤러 컨베어 (체인 구동 롤러)');
    L.push(`   롤러 수 = L/피치 + 1 = ${i.length * 1000}/${i.rollerPitch} + 1 = ${nR} 개, 롤러 총질량 = ${f(mR)} kg`);
    L.push(`   굴림 저항 = μ1·g·m·cosθ = ${i.frictionRolling}·9.81·${f(m)}·${f(cos, 3)} = ${f(fRoll, 1)} N`);
    L.push(`   베어링 저항 = μ2·g·(m·cosθ + mR)·(d축/D1) = ${f(fBear, 1)} N`);
    L.push(`   구동체인 저항(개략) = 0.1·g·m체인·0.5 = ${f(fCh, 1)} N (체인 ${f(mC)} kg)`);
    L.push(`   경사 저항 = g·m·sinθ = ${f(fLift, 1)} N`);
    if (i.accumulation) L.push(`   축적 저항 = μST·g·m = ${f(fAcc, 1)} N`);
    L.push(`   유효장력 FU = ${f(fu, 1)} N`);
    r.add('load', '총 적재하중 m', f(m), 'kg');
    r.add('load', '롤러 수 / 총질량', `${nR} 개 / ${f(mR, 1)} kg`, '');
    r.add('load', '유효장력 FU', f(fu, 1), 'N', `굴림 ${f(fRoll, 1)} + 베어링 ${f(fBear, 1)} + 체인 ${f(fCh, 1)} + 경사 ${f(fLift, 1)}` + (i.accumulation ? ` + 축적 ${f(fAcc, 1)}` : ''));
    r.add('load', '롤러 외경 D1', f(D1, 1), 'mm', `롤러 회전수 ${f(i.speed * 1000 / (Math.PI * D1), 1)} rpm`);
    if (nR > 40) r.warn(`롤러 ${nR}개를 체인 1계통으로 구동하면 전동 손실이 큽니다. 구동 구간 분할을 검토하십시오.`);
    const rr = D1 / 2000;
    return { fu, D1, J: m * rr * rr + 0.8 * mR * rr * rr + mC * rr * rr, basis: `롤러 외경 ${D1} mm 기준`, rpmToV: rpm => rpm * Math.PI * D1 / 1000 };
  }

  function chainLoad(i, r, L) {
    const m = i.workMass * i.workCount, mC = i.chainMassPerM * i.chainStrands * 2 * i.length;
    const cos = Math.cos(i.angle * Math.PI / 180), sin = Math.sin(i.angle * Math.PI / 180);
    const fFric = i.frictionChain * G * (m + mC) * cos, fLift = G * m * sin, fAcc = i.accumulation ? i.frictionProduct * G * m : 0;
    const fu = fFric + fLift + fAcc, D1 = pcd(i.chainPitch, i.conveyorSprocketTeeth);
    const spec = chainByPitch(i.chainPitch), allow = i.chainAllowableLoad > 0 ? i.chainAllowableLoad : (spec ? spec.allow : 0), tens = spec ? spec.tensile : 0;
    const fS = fu / i.chainStrands * 1.1;
    L.push('[1] 부하 — 체인 컨베어');
    L.push(`   체인 질량 mC = ${i.chainMassPerM} × ${i.chainStrands} × ${2 * i.length} = ${f(mC)} kg`);
    L.push(`   마찰 저항 = μ·g·(m+mC)·cosθ = ${i.frictionChain}·9.81·(${f(m)}+${f(mC)})·${f(cos, 3)} = ${f(fFric, 1)} N`);
    L.push(`   경사 저항 = g·m·sinθ = ${f(fLift, 1)} N`);
    if (i.accumulation) L.push(`   축적 저항 = μST·g·m = ${f(fAcc, 1)} N`);
    L.push(`   유효장력 FU = ${f(fu, 1)} N`);
    L.push(`   스프라켓 PCD D1 = p / sin(180°/z) = ${i.chainPitch} / sin(180°/${i.conveyorSprocketTeeth}) = ${f(D1)} mm`);
    L.push(`   열당 체인 장력 = FU / 열수 × 1.1 = ${f(fS, 1)} N` + (allow > 0 ? ` (허용 ${allow} kN → ${fS <= allow * 1000 ? 'OK' : 'NG'}` + (tens > 0 ? `, 안전율 ${f(tens * 1000 / fS, 1)})` : ')') : ''));
    r.add('load', '총 적재하중 m', f(m), 'kg');
    r.add('load', '체인 질량 mC', f(mC), 'kg', `${i.chainStrands}열, 상·하 양측`);
    r.add('load', '유효장력 FU', f(fu, 1), 'N', `마찰 ${f(fFric, 1)} + 경사 ${f(fLift, 1)}` + (i.accumulation ? ` + 축적 ${f(fAcc, 1)}` : ''));
    r.add('load', '컨베어 스프라켓 PCD D1', f(D1), 'mm', `피치 ${i.chainPitch} × ${i.conveyorSprocketTeeth}T`);
    r.add('load', '열당 체인 장력', f(fS, 1), 'N', allow > 0 ? `허용 ${allow} kN (${spec ? spec.no : '입력'}) → ${fS <= allow * 1000 ? 'OK' : 'NG'}` : '허용하중 미입력 — 카탈로그 확인', allow > 0 ? (fS <= allow * 1000 ? 'ok' : 'ng') : '');
    if (allow > 0 && fS > allow * 1000) r.warn('컨베어 체인 장력이 허용하중을 초과합니다. 체인 호수 상향 또는 열수 추가 필요.');
    if (tens > 0 && tens * 1000 / fS < 7) r.warn('컨베어 체인 인장강도 대비 안전율이 7 미만입니다.');
    const rr = D1 / 2000;
    return { fu, D1, J: (m + mC) * rr * rr + i.chainStrands * i.conveyorSprocketMass * rr * rr, basis: `피치 ${i.chainPitch} × ${i.conveyorSprocketTeeth}T 기준`, rpmToV: rpm => rpm * i.chainPitch * i.conveyorSprocketTeeth / 1000 };
  }

  // ───────────── 보조 ─────────────
  function bestRatioAndTeeth(nMotor, n1) {
    let best = Infinity, sel = [D.reducerRatios[0], 15, 15];
    for (const ratio of D.reducerRatios) { const nOut = nMotor / ratio;
      for (const z3 of D.teeth) { if (z3 < 13 || z3 > 25) continue;
        for (const z2 of D.teeth) { const iTr = z2 / z3; if (iTr < 1 || iTr > 4) continue;
          const sc = Math.abs(nOut / iTr - n1) / n1 + iTr * 0.0005 - z3 * 0.00001;
          if (sc < best) { best = sc; sel = [ratio, z3, z2]; } } } }
    return sel;
  }
  function bestTeethPair(need) {
    let best = Infinity, sel = [15, 15];
    for (const z3 of D.teeth) { if (z3 < 13 || z3 > 25) continue;
      for (const z2 of D.teeth) { const e = Math.abs(z2 / z3 - need) / need - z3 * 0.00001; if (e < best) { best = e; sel = [z3, z2]; } } }
    return sel;
  }
  const actualC = (links, z2, z3, p) => { const a = links - (z2 + z3) / 2, b = ((z2 - z3) / (2 * Math.PI)) ** 2; return p / 4 * (a + Math.sqrt(a * a - 8 * b)); };

  // ───────────── 유도전동기 ─────────────
  function selectInduction(i, ls, n1, T1, PA, r, L) {
    const cd = i.transmission === 'chain', etaTr = cd ? i.transmissionEfficiency : 1, eta = i.reducerEfficiency * etaTr;
    const pReq = PA / eta * i.motorMargin; let pMotor = ceilStd(pReq, D.motorKw);
    const nMotor = motorRpm(i.motorPoles, i.frequency), s = sf(i.loadClass, i.dailyHours);
    L.push('[3] 모터 (유도전동기)');
    L.push(`   η = ${i.reducerEfficiency} × ${etaTr} = ${f(eta, 3)}`);
    L.push(`   P필요 = PA / η × 여유율 = ${f(PA, 3)} / ${f(eta, 3)} × ${i.motorMargin} = ${f(pReq, 3)} kW → 표준 ${pMotor} kW, ${i.motorPoles}P, ${i.frequency} Hz, 약 ${nMotor} rpm`);
    L.push(`   서비스팩터 Sf = ${s} (${names.load[i.loadClass]}, ${names.hours[i.dailyHours]})`);
    let iTr = 1, z3 = 0, z2 = 0, ratio;
    if (cd && !i.autoSelectSprockets) { z3 = i.driveSprocketTeeth; z2 = i.drivenSprocketTeeth; iTr = z2 / z3; }
    if (i.manualReducerRatio > 0) { ratio = i.manualReducerRatio; if (cd && i.autoSelectSprockets) { [z3, z2] = bestTeethPair(nMotor / ratio / n1); iTr = z2 / z3; } }
    else if (cd && i.autoSelectSprockets) { [ratio, z3, z2] = bestRatioAndTeeth(nMotor, n1); iTr = z2 / z3; }
    else ratio = nearStd(nMotor / (n1 * iTr), D.reducerRatios);
    const nOut = nMotor / ratio, n1Act = nOut / iTr, vAct = ls.rpmToV(n1Act), err = (vAct - i.speed) / i.speed * 100, tOut = T1 / (iTr * etaTr);
    L.push(''); L.push('[4] 감속비 / 스프라켓');
    L.push(cd ? `   전동 스프라켓 z3 = ${z3} T, z2 = ${z2} T → 전동비 i2 = ${f(iTr, 3)}` : '   직결 (전동비 1.0)');
    L.push(`   필요 총감속비 = ${nMotor} / ${f(n1)} = ${f(nMotor / n1)};  감속기 i1 = ${ratio}, 출력 ${f(nOut)} rpm`);
    L.push(`   실제 구동축 회전수 ${f(n1Act)} rpm → 실제 속도 ${f(vAct)} m/min (오차 ${pct(err)})`);
    L.push(`   감속기 출력축 필요 토크 = T1 / (i2·η전동) = ${f(T1)} / (${f(iTr, 3)}·${etaTr}) = ${f(tOut)} N·m`);
    let tAvail = 9550 * pMotor * i.reducerEfficiency / nOut, bumps = 0;
    while (tOut * s > tAvail && bumps < 5) { const nx = ceilStd(pMotor * 1.01, D.motorKw); if (nx <= pMotor) break; pMotor = nx; bumps++; tAvail = 9550 * pMotor * i.reducerEfficiency / nOut; }
    if (bumps) { L.push(`   ※ 출력토크 부족으로 모터를 ${pMotor} kW 로 상향`); r.warn(`서비스팩터 적용 시 출력토크가 부족하여 모터를 ${pMotor} kW로 상향했습니다.`); }
    const loadRate = tOut * s / tAvail * 100, tRated = 9550 * pMotor / nMotor;
    L.push(`   기어모터 정격 출력토크 = 9550·${pMotor}·${i.reducerEfficiency} / ${f(nOut)} = ${f(tAvail)} N·m,  부하율 = ${f(tOut)}·${s} / ${f(tAvail)} = ${f(loadRate, 1)} %`);
    r.add('motor', '모터 종류', '3상 유도전동기', '');
    r.add('motor', '필요 동력 (여유율 포함)', f(pReq, 3), 'kW', `PA/η×${i.motorMargin}`);
    r.add('motor', '선정 모터', `${pMotor} kW ${i.motorPoles}P`, '', `${i.frequency} Hz, 약 ${nMotor} rpm`, 'key');
    r.add('motor', '모터 정격 토크', f(tRated), 'N·m');
    r.add('motor', '서비스팩터 Sf', f(s), '', `${names.load[i.loadClass]} / ${names.hours[i.dailyHours]}`);
    r.add('reducer', '감속비 i1', `1/${f(ratio, 1)}`, '', `필요 총감속비 ${f(nMotor / n1)}`, 'key');
    r.add('reducer', '출력 회전수', f(nOut), 'rpm');
    r.add('reducer', '출력축 필요 토크', f(tOut), 'N·m');
    r.add('reducer', '출력축 정격 토크 (모터 기준)', f(tAvail), 'N·m', '9550·P·η/n');
    r.add('reducer', '토크 부하율 (Sf 적용)', f(loadRate, 1), '%', loadRate <= 100 ? 'OK' : 'NG — 용량 상향 필요', loadRate <= 100 ? 'ok' : 'ng');
    if (loadRate > 100) r.warn('감속기 출력토크 부하율이 100%를 초과합니다.');
    return { kw: pMotor, rpm: nMotor, tRated, ratio, z3, z2, iTr, nOut, tOut, vAct, err };
  }

  // ───────────── 서보 ─────────────
  function selectServo(i, ls, n1, T1, r, L) {
    const cd = i.transmission === 'chain', etaTr = cd ? i.transmissionEfficiency : 1, eta = i.reducerEfficiency * etaTr, nMax = i.servoRatedRpm * i.servoSpeedUtilization;
    const v = i.speed / 60, ta = i.accelTime, td = i.decelTime; let tdw = i.dwellTime, tc = 0, tri = false, vPeak = v;
    const cont = !(i.moveDistance > 0);
    if (!cont) { const s = i.moveDistance / 1000; tc = (s - v * (ta + td) / 2) / v; if (tc < 0) { tri = true; tc = 0; vPeak = 2 * s / (ta + td); } } else tdw = 0;
    const tCy = ta + tc + td + tdw;
    L.push('[3] 모터 (서보모터)');
    L.push(`   정격 ${i.servoRatedRpm} rpm, 최고 사용 회전수 ${f(nMax, 0)} rpm, 최대토크 ×${i.servoMaxTorqueRatio}, 실효토크 ≤ 정격×${i.servoTorqueUtilization}, 허용 관성비 ${i.servoMaxInertiaRatio}${i.servoInertiaAsWarning ? ' (경고만)' : ''}`);
    L.push(cont ? `   운전 패턴: 연속 운전 (가속 ${ta} s)` : `   운전 패턴: 이동 ${i.moveDistance} mm — 가속 ${ta} s, 정속 ${f(tc, 3)} s, 감속 ${td} s, 정지 ${tdw} s → 사이클 ${f(tCy, 3)} s` + (tri ? ` ※ 삼각 프로파일 (최고속도 ${f(vPeak * 60)} m/min)` : ''));
    const ratios = i.manualReducerRatio > 0 ? [i.manualReducerRatio] : D.servoRatios;
    const pairs = [];
    if (!cd) pairs.push([0, 0]); else if (!i.autoSelectSprockets) pairs.push([i.driveSprocketTeeth, i.drivenSprocketTeeth]);
    else for (const z3 of D.teeth) { if (z3 < 13 || z3 > 25) continue; for (const z2 of D.teeth) if (z2 / z3 >= 1 && z2 / z3 <= 4) pairs.push([z3, z2]); }
    const n1Peak = n1 * vPeak / v;
    let best = null, bi = Infinity, bs = Infinity, anySpeed = false;
    for (const ratio of ratios) for (const [z3, z2] of pairs) {
      const iTr = z3 === 0 ? 1 : z2 / z3, nm = n1Peak * iTr * ratio; if (nm > nMax) continue; anySpeed = true;
      const jl = ls.J / (iTr * iTr * ratio * ratio), tl = T1 / (iTr * ratio * eta), w = 2 * Math.PI * nm / 60;
      for (let k = 0; k < D.servos.length; k++) {
        const [kw, jm] = D.servos[k], jr = jl / jm; if (jr > i.servoMaxInertiaRatio && !i.servoInertiaAsWarning) continue;
        const tr = 9550 * kw / i.servoRatedRpm, jt = jm + jl, tacc = jt * w / ta + tl, tdec = Math.abs(jt * w / td - tl);
        const trms = cont ? tl : Math.sqrt((tacc * tacc * ta + tl * tl * tc + tdec * tdec * td) / tCy);
        if (Math.max(tacc, tdec) > tr * i.servoMaxTorqueRatio) continue; if (trms > tr * i.servoTorqueUtilization) continue;
        const sc = k * 1000 + jr * 10 + ratio * 0.01;
        if (k < bi || (k === bi && sc < bs)) { bi = k; bs = sc; best = { kw, jm, ratio, z3, z2, iTr, jr, jl, tl, tacc, tdec, trms, nm, tr, w }; }
        break;
      }
    }
    if (!best) throw new Error(anySpeed ? '조건(관성비·토크)을 만족하는 서보 용량이 없습니다. 감속비 범위 확대, 가감속 시간 연장, 허용 관성비 상향을 검토하십시오.'
      : `모든 감속비에서 모터 회전수가 최고 사용 회전수(${f(nMax, 0)} rpm)를 초과합니다. 구동 풀리/스프라켓 지름을 키우거나 정격 회전수·회전수 사용률을 올리십시오.`);
    const b = best, nOut = b.nm / b.ratio, tOut = T1 / (b.iTr * etaTr), vAct = vPeak * 60, name = b.kw < 1 ? `${b.kw * 1000} W` : `${b.kw} kW`, tMax = b.tr * i.servoMaxTorqueRatio;
    L.push(''); L.push('[4] 감속비 / 스프라켓 (서보: 관성비·회전수 조건으로 선정)');
    L.push(cd ? `   전동 스프라켓 z3 = ${b.z3} T, z2 = ${b.z2} T → i2 = ${f(b.iTr, 3)}` : '   직결 (전동비 1.0)');
    L.push(`   감속비 i1 = ${b.ratio}${b.ratio === 1 ? ' (감속기 없음)' : ''}, 모터 회전수 = ${f(n1Peak)}·${f(b.iTr, 3)}·${b.ratio} = ${f(b.nm, 0)} rpm ≤ ${f(nMax, 0)}`);
    L.push(`   모터축 환산 부하 관성 JL = ${f(ls.J, 4)} / (${f(b.iTr, 3)}²·${b.ratio}²) = ${b.jl.toExponential(3)} kg·m²`);
    L.push(`   관성비 = JL / JM = ${b.jl.toExponential(3)} / ${b.jm.toExponential(3)} = ${f(b.jr)} (허용 ${i.servoMaxInertiaRatio}) → ${b.jr <= i.servoMaxInertiaRatio ? 'OK' : '초과 — 경고만'}`);
    L.push(`   부하 토크 TL = T1 / (i2·i1·η) = ${f(T1)} / (${f(b.iTr, 3)}·${b.ratio}·${f(eta, 3)}) = ${f(b.tl, 3)} N·m`);
    L.push(`   가속 토크 TA = (JM+JL)·ω/ta + TL = ${f(b.tacc, 3)} N·m,  감속 토크 TD = |(JM+JL)·ω/td − TL| = ${f(b.tdec, 3)} N·m`);
    L.push(cont ? `   실효 토크 (연속) = TL = ${f(b.trms, 3)} N·m` : `   실효 토크 Trms = √[(TA²·ta + TL²·tc + TD²·td)/t사이클] = ${f(b.trms, 3)} N·m`);
    L.push(`   → 서보 ${name}: 정격토크 ${f(b.tr, 3)} N·m, 최대토크 ${f(tMax)} N·m;  실효/정격 ${f(b.trms / b.tr * 100, 1)} %, 최대/최대허용 ${f(Math.max(b.tacc, b.tdec) / tMax * 100, 1)} %`);
    r.add('motor', '모터 종류', 'AC 서보모터', '');
    r.add('motor', '선정 서보 용량', name, '', `정격 ${i.servoRatedRpm} rpm, 로터 관성 ${f(b.jm * 1e4, 3)}×10⁻⁴ kg·m² (대표값)`, 'key');
    r.add('motor', '정격 토크 / 최대 토크', `${f(b.tr)} / ${f(tMax)}`, 'N·m');
    r.add('motor', '운전 시 모터 회전수', f(b.nm, 0), 'rpm', `≤ ${f(nMax, 0)} rpm (정격×${i.servoSpeedUtilization})`);
    r.add('motor', '부하 토크 TL (모터축)', f(b.tl, 3), 'N·m', `정격 대비 ${f(b.tl / b.tr * 100, 1)} %`);
    r.add('motor', '가속 토크 TA', f(b.tacc, 3), 'N·m', `최대토크 대비 ${f(b.tacc / tMax * 100, 1)} %`);
    r.add('motor', '감속 토크 TD', f(b.tdec, 3), 'N·m');
    r.add('motor', '실효 토크 Trms', f(b.trms, 3), 'N·m', `정격 대비 ${f(b.trms / b.tr * 100, 1)} % (허용 ${i.servoTorqueUtilization * 100} %)`, 'ok');
    r.add('motor', '관성비 JL/JM', f(b.jr), '', b.jr <= i.servoMaxInertiaRatio ? `허용 ≤ ${i.servoMaxInertiaRatio} → OK` : `허용 ${i.servoMaxInertiaRatio} 초과 — 게인 낮게, 가감속 완만하게`, b.jr <= i.servoMaxInertiaRatio ? 'ok' : 'ng');
    if (b.jr > i.servoMaxInertiaRatio) r.warn(`관성비 ${f(b.jr, 1)}가 허용값 ${i.servoMaxInertiaRatio}를 초과합니다(경고만 설정). 저속·완만한 가감속 조건에서만 사용하십시오.`);
    else if (b.jr > 5) r.warn(`관성비 ${f(b.jr, 1)} — 5를 넘으면 게인 조정 여유가 줄어듭니다. 감속비 상향 또는 중관성 모터를 검토하십시오.`);
    if (!cont) r.add('motor', '운전 사이클', `${ta}+${f(tc)}+${td}+${tdw}`, 's', tri ? `삼각 프로파일 — 최고속도 ${f(vAct)} m/min` : `이동거리 ${i.moveDistance} mm`);
    if (tri) r.warn(`이동거리가 짧아 지정 속도(${i.speed} m/min)에 도달하지 못합니다. 실제 최고속도 ${f(vAct)} m/min 로 계산했습니다.`);
    r.add('reducer', '감속비 i1', b.ratio === 1 ? '직결 (감속기 없음)' : `1/${f(b.ratio, 1)}`, '', '서보용 유성감속기 등', 'key');
    r.add('reducer', '출력 회전수', f(nOut), 'rpm');
    r.add('reducer', '출력축 필요 토크 (정상)', f(tOut), 'N·m');
    r.add('reducer', '출력축 토크 (가속 시)', f(b.tacc * b.ratio * i.reducerEfficiency), 'N·m', '감속기 순시 허용토크와 비교');
    return { kw: b.kw, rpm: b.nm, tRated: b.tr, ratio: b.ratio, z3: b.z3, z2: b.z2, iTr: b.iTr, nOut, tOut, vAct, err: (vAct - i.speed) / i.speed * 100 };
  }

  function chainStage(i, d, r, L) {
    const k1 = k1chain(i.loadClass); let pD = d.kw * k1;
    if (i.motorKind === 'servo') pD = Math.max(pD, d.tOut * d.nOut / 9550 * k1);
    let sel = null, rating = 0;
    for (const c of D.chains) { rating = chainRating(c, d.nOut, d.z3); if (rating >= pD) { sel = c; break; } }
    sel = sel || D.chains[D.chains.length - 1];
    const dp3 = pcd(sel.pitch, d.z3), dp2 = pcd(sel.pitch, d.z2), F = 2000 * d.tOut / dp3, C = i.transmissionCenterDistance;
    const links = (d.z2 + d.z3) / 2 + 2 * C / sel.pitch + ((d.z2 - d.z3) / (2 * Math.PI)) ** 2 * sel.pitch / C;
    let le = Math.ceil(links); if (le % 2) le++;
    const cA = actualC(le, d.z2, d.z3, sel.pitch), csf = sel.tensile * 1000 / F, k2 = k2ohl(i.ohlPosition), ohl = F * k2;
    L.push(''); L.push('[5] 전동 체인 / 스프라켓');
    L.push(`   설계동력 = P × K1(${k1}) = ${f(pD, 3)} kW;  소스프라켓 ${d.z3}T @ ${f(d.nOut)} rpm → ${sel.no} 정격 ${f(rating, 3)} kW`);
    L.push(`   피치원경 Dp3 = ${f(dp3)} mm, Dp2 = ${f(dp2)} mm;  체인 장력 F = 2000·${f(d.tOut)} / ${f(dp3)} = ${f(F, 1)} N (안전율 ${f(csf, 1)})`);
    L.push(`   링크수 = ${f(links)} → ${le} 링크 (짝수), 실제 축간거리 ${f(cA, 1)} mm;  OHL = F × K1 × K2 = ${f(F, 1)} × 1.0 × ${k2} = ${f(ohl, 1)} N (${names.ohl[i.ohlPosition]})`);
    r.add('sprocket', '전동 체인', sel.no, '', `피치 ${sel.pitch} mm, 정격 ${f(rating)} kW (설계동력 ${f(pD)} kW)`, 'key');
    r.add('sprocket', '스프라켓 피치원경 Dp3 / Dp2', `${f(dp3, 1)} / ${f(dp2, 1)}`, 'mm');
    r.add('sprocket', '체인 링크수', String(le), '링크', `축간거리 ${f(cA, 1)} mm (입력 ${C} mm)`);
    r.add('sprocket', '체인 장력', f(F, 1), 'N', `안전율 ${f(csf, 1)} (권장 ≥ 7)`, csf >= 7 ? 'ok' : 'ng');
    if (csf < 7) r.warn(`전동 체인 안전율 ${f(csf, 1)} < 7. 체인 호수 상향 또는 스프라켓 대경화 검토.`);
    if (d.z3 < 15) r.warn('감속기측 스프라켓 잇수가 15T 미만입니다. 현운동과 마모 증가에 주의.');
    r.add('check', '감속기 출력축 OHL', f(ohl, 1), 'N', `K2=${k2}, 카탈로그 허용 OHL 과 비교`);
    return { chain: sel.no, links: le };
  }

  function startupCheck(i, ls, d, r, L) {
    const c = cFactor(i.transmission, i.startsPerDay), jM = ls.J / (d.iTr * d.iTr) / (d.ratio * d.ratio), jC = jM * c;
    const w = 2 * Math.PI * d.rpm / 60, tL = d.tOut / (d.ratio * i.reducerEfficiency), tA = jM * w / i.accelTime, tS = tL + tA, sr = tS / d.tRated;
    L.push(''); L.push('[6] 관성 / 기동 검토');
    L.push(`   모터축 환산 J = ${f(ls.J, 4)} / (${f(d.iTr, 3)}²·${d.ratio}²) = ${jM.toExponential(3)} kg·m²;  보정계수 C = ${c} → ${jC.toExponential(3)}`);
    L.push(`   기동 토크 = T부하 + J·ω/t = ${f(tL, 3)} + ${jM.toExponential(2)}·${f(w, 1)}/${i.accelTime} = ${f(tS, 3)} N·m → 정격 대비 ${f(sr * 100, 1)} %`);
    r.add('check', '모터축 환산 관성 (C 보정)', jC.toExponential(3), 'kg·m²', `C = ${c}; 모터 허용 부하관성과 비교`);
    r.add('check', '기동 토크 비율', f(sr * 100, 1), '%', sr <= 1.5 ? 'OK (≤150%)' : '확인 필요 — 가속시간 연장 또는 용량 상향', sr <= 1.5 ? 'ok' : 'ng');
    if (sr > 1.5) r.warn('기동 시 필요 토크가 모터 정격의 150%를 초과합니다.');
  }

  function calculate(i) {
    const rows = [], warnings = [], L = [];
    const r = { add: (g, item, value, unit, note = '', tag = '') => rows.push({ g, item, value, unit, note, tag }), warn: m => warnings.push(m) };
    const out = { ok: true, rows, warnings, log: '', summary: {} };
    try {
      validate(i);
      L.push(`■ ${i.projectName} — ${typeName(i)} / ${i.motorKind === 'servo' ? '서보모터' : '유도전동기'} 계산 근거`);
      L.push(`   V = ${i.speed} m/min, L = ${i.length} m, θ = ${i.angle}°, M1 = ${i.workMass} kg × n1 = ${i.workCount} → m = ${f(i.workMass * i.workCount)} kg`); L.push('');
      const ls = i.type === 'belt' ? beltLoad(i, r, L) : i.type === 'roller' ? rollerLoad(i, r, L) : chainLoad(i, r, L);
      const n1 = i.type === 'chain' ? i.speed * 1000 / (i.chainPitch * i.conveyorSprocketTeeth) : i.speed * 1000 / (Math.PI * ls.D1);
      const T1 = ls.fu * ls.D1 / 2000, PA = ls.fu * i.speed / 60000;
      L.push(''); L.push('[2] 구동축');
      L.push(i.type === 'chain' ? `   n1 = V·1000/(p·z) = ${f(n1)} rpm` : `   n1 = V·1000/(π·D1) = ${i.speed}·1000/(π·${f(ls.D1)}) = ${f(n1)} rpm`);
      L.push(`   T1 = FU·D1/2 = ${f(ls.fu, 1)} × ${f(ls.D1 / 2000, 4)} = ${f(T1)} N·m;  PA = FU·V/60000 = ${f(PA, 3)} kW;  J = ${f(ls.J, 4)} kg·m² (GD² ${f(4 * ls.J, 3)})`); L.push('');
      r.add('shaft', '구동축 회전수 n1', f(n1), 'rpm', ls.basis);
      r.add('shaft', '구동축 토크 T1', f(T1), 'N·m');
      r.add('shaft', '축동력 PA', f(PA, 3), 'kW');
      r.add('shaft', '구동축 환산 관성 GD²', f(4 * ls.J, 3), 'kg·m²', `J = ${f(ls.J, 4)} kg·m²`);
      const d = i.motorKind === 'servo' ? selectServo(i, ls, n1, T1, r, L) : selectInduction(i, ls, n1, T1, PA, r, L);
      const cd = i.transmission === 'chain';
      r.add('sprocket', '전동 방식', cd ? '체인 전동' : '직결', '');
      if (cd) { r.add('sprocket', '감속기측 스프라켓 z3', String(d.z3), 'T'); r.add('sprocket', '구동축측 스프라켓 z2', String(d.z2), 'T'); r.add('sprocket', '전동비 i2 = z2/z3', f(d.iTr, 3), ''); }
      r.add('sprocket', '실제 반송속도', f(d.vAct), 'm/min', i.motorKind === 'servo' ? '서보 속도지령으로 맞춤' : `오차 ${pct(d.err)}`, i.motorKind !== 'servo' && Math.abs(d.err) > 5 ? 'ng' : '');
      if (i.motorKind !== 'servo' && Math.abs(d.err) > 5) r.warn(`실제 속도 오차가 ${pct(d.err)} 입니다. 감속비 또는 스프라켓 잇수를 조정하십시오.`);
      let ch = null; if (cd) ch = chainStage(i, d, r, L);
      if (i.motorKind !== 'servo') startupCheck(i, ls, d, r, L);
      r.add('check', '참고', '표준 규격 데이터 기반', '', '최종 선정은 제조사 카탈로그(허용토크·OHL·관성)로 확인');
      out.summary = { kw: d.kw, rpm: d.rpm, ratio: d.ratio, nOut: d.nOut, tOut: d.tOut, z3: d.z3, z2: d.z2, chain: ch, vAct: d.vAct, err: d.err, servo: i.motorKind === 'servo', poles: i.motorPoles };
    } catch (e) { out.ok = false; warnings.push('계산 오류: ' + e.message); }
    out.log = L.join('\n');
    return out;
  }

  global.ConveyorEngine = { calculate, defaults, typeName, data: D, fmt: f };
})(window);
