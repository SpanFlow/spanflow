// E73-T11: writes the bundled EDI guides (Guides/*.json) from the compact tables below. The JSON files are the
// shipped data (embedded in the assembly); this script only keeps them easy to read and change. Run:
//   node src/SpanFlow.Mapping.Formats/Guides/generate.js
// Element spec: "name" | "name*" (required) followed by optional "type min max" and "=CODE|CODE".
// A composite is ["name", "component", "component*", ...]. Segment: S(id, min, max, elements, key?).
// Loop: L(name, min, max, ...items). Common subsets of the standard, named for mapping; add what a partner needs.
const fs = require('fs');
const path = require('path');

function el(spec) {
  if (Array.isArray(spec)) {
    const [head, ...parts] = spec;
    const base = el(head);
    base.components = parts.map(el);
    return base;
  }
  const [main, codes] = spec.split('=');
  const [nameRaw, type, min, max] = main.trim().split(/\s+/);
  const out = { name: nameRaw.replace('*', '') };
  if (nameRaw.endsWith('*')) out.required = true;
  if (type) out.type = type;
  if (min) out.min = Number(min);
  if (max) out.max = Number(max);
  if (codes) out.codes = codes.split('|');
  return out;
}
const S = (id, min, max, elements, name) => ({ id, ...(name ? { name } : {}), ...(min ? { min } : {}), ...(max !== 1 ? { max } : {}), elements: elements.map(el) });
const L = (loop, min, max, ...segments) => ({ loop, ...(min ? { min } : {}), max, segments });

// ---------------------------------------------------------------- X12 004010 building blocks
const REF = (max = 12) => S('REF', 0, max, ['qualifier* id 2 3', 'value an 1 30', 'description an 1 80']);
const PER = () => S('PER', 0, 3, ['contactFunction* id 2 2', 'name an 1 60', 'commQualifier1 id 2 2', 'commNumber1 an 1 80', 'commQualifier2 id 2 2', 'commNumber2 an 1 80']);
const DTM = (max = 10) => S('DTM', 0, max, ['qualifier* id 3 3', 'date dt 8 8', 'time tm 4 8']);
const N1Loop = (max = 200) => L('N1Loop', 0, max,
  S('N1', 1, 1, ['entityId* id 2 3', 'name an 1 60', 'idQualifier id 1 2', 'id an 2 80']),
  S('N2', 0, 2, ['name1* an 1 60', 'name2 an 1 60']),
  S('N3', 0, 2, ['address1* an 1 55', 'address2 an 1 55']),
  S('N4', 0, 1, ['city an 2 30', 'state id 2 2', 'postalCode id 3 15', 'country id 2 3']),
  REF(), PER());
const PIDLoop = () => L('PIDLoop', 0, 1000,
  S('PID', 1, 1, ['type* id 1 1 =F|S|X', 'characteristic id 2 3', 'agency id 2 2', 'descriptionCode an 1 12', 'description an 1 80']));
const productIds = ['idQualifier1 id 2 2', 'productId1 an 1 48', 'idQualifier2 id 2 2', 'productId2 an 1 48', 'idQualifier3 id 2 2', 'productId3 an 1 48'];
const CTT = () => S('CTT', 0, 1, ['lineCount* n 1 6', 'hashTotal r 1 10']);

const x12 = (message, title, segments) => ({ name: `x12-${message}`, standard: 'x12', message, version: '004010', title, segments });

const x850 = x12('850', 'Purchase order', [
  S('BEG', 1, 1, ['purposeCode* id 2 2 =00|01|05|06|07', 'typeCode* id 2 2', 'poNumber* an 1 22', 'releaseNumber an 1 30', 'date* dt 8 8', 'contractNumber an 1 30']),
  S('CUR', 0, 1, ['entityId* id 2 3', 'currency* id 3 3']),
  REF(99), PER(), DTM(),
  S('TD5', 0, 12, ['routingSequence id 1 2', 'idQualifier id 1 2', 'carrierId an 2 80', 'transportMethod id 1 2', 'routing an 1 35']),
  N1Loop(),
  L('PO1Loop', 1, 100000,
    S('PO1', 1, 1, ['lineNumber an 1 20', 'quantity r 1 15', 'unit id 2 2', 'unitPrice r 1 17', 'basisOfPrice id 2 2', ...productIds]),
    S('CUR', 0, 1, ['entityId* id 2 3', 'currency* id 3 3']),
    PIDLoop(),
    S('PO4', 0, 1, ['pack n 1 6', 'size r 1 8', 'unit id 2 2']),
    REF(), DTM()),
  L('CTTLoop', 0, 1, CTT(), S('AMT', 0, 1, ['qualifier* id 1 3', 'amount* r 1 18'])),
]);

const x810 = x12('810', 'Invoice', [
  S('BIG', 1, 1, ['date* dt 8 8', 'invoiceNumber* an 1 22', 'poDate dt 8 8', 'poNumber an 1 22', 'releaseNumber an 1 30', 'changeOrderSequence an 1 8', 'transactionType id 2 2']),
  S('CUR', 0, 1, ['entityId* id 2 3', 'currency* id 3 3']),
  REF(), PER(), N1Loop(),
  S('ITD', 0, 999, ['termsType id 2 2', 'basisDate id 1 2', 'discountPercent r 1 6', 'discountDueDate dt 8 8', 'discountDays n 1 3', 'netDueDate dt 8 8', 'netDays n 1 3', 'discountAmount n 1 10']),
  DTM(),
  L('IT1Loop', 1, 200000,
    S('IT1', 1, 1, ['lineNumber an 1 20', 'quantity r 1 10', 'unit id 2 2', 'unitPrice r 1 17', 'basisOfPrice id 2 2', ...productIds]),
    PIDLoop(), REF(), DTM()),
  S('TDS', 1, 1, ['amount* n 1 15', 'amountSubjectToDiscount n 1 15', 'discountedAmount n 1 15', 'termsDiscount n 1 15']),
  S('TXI', 0, 10, ['taxType* id 2 2', 'amount r 1 18', 'percent r 1 10']),
  L('SACLoop', 0, 25, S('SAC', 1, 1, ['indicator* id 1 1 =A|C|N', 'code id 4 4', 'agency id 2 2', 'agencyCode an 1 10', 'amount n 1 15'])),
  CTT(),
]);

const x856 = x12('856', 'Ship notice / manifest', [
  S('BSN', 1, 1, ['purposeCode* id 2 2', 'shipmentId* an 2 30', 'date* dt 8 8', 'time* tm 4 8', 'structureCode id 4 4']),
  DTM(),
  L('HLLoop', 1, 200000,
    S('HL', 1, 1, ['id* an 1 12', 'parentId an 1 12', 'levelCode* id 1 2 =S|O|P|T|I|Q|F', 'childCode id 1 1 =0|1']),
    S('LIN', 0, 1, ['lineNumber an 1 20', ...productIds]),
    S('SN1', 0, 1, ['lineNumber an 1 20', 'quantity* r 1 10', 'unit* id 2 2', 'quantityShipped r 1 15']),
    S('PRF', 0, 1, ['poNumber* an 1 22', 'releaseNumber an 1 30', 'changeOrderSequence an 1 8', 'poDate dt 8 8']),
    S('PO4', 0, 1, ['pack n 1 6', 'size r 1 8', 'unit id 2 2']),
    PIDLoop(),
    S('TD1', 0, 20, ['packagingCode an 3 5', 'ladingQuantity n 1 7', 'commodityQualifier id 1 1', 'commodityCode an 1 30', 'description an 1 50', 'weightQualifier id 1 2', 'weight r 1 10', 'weightUnit id 2 2']),
    S('TD5', 0, 12, ['routingSequence id 1 2', 'idQualifier id 1 2', 'carrierId an 2 80', 'transportMethod id 1 2', 'routing an 1 35']),
    REF(200), DTM(),
    N1Loop(),
    S('MAN', 0, 999, ['qualifier* id 1 2', 'marks* an 1 48'])),
  CTT(),
]);

const x997 = x12('997', 'Functional acknowledgement', [
  S('AK1', 1, 1, ['functionalIdCode* id 2 2', 'groupControlNumber* n 1 9']),
  L('AK2Loop', 0, 999999,
    S('AK2', 1, 1, ['code* id 3 3', 'controlNumber* an 4 9']),
    L('AK3Loop', 0, 999999,
      S('AK3', 1, 1, ['segmentId* id 2 3', 'position* n 1 6', 'loopId an 1 6', 'errorCode id 1 3']),
      S('AK4', 0, 99, [['position*', 'element* n 1 2', 'component n 1 2'], 'elementReference n 1 4', 'errorCode* id 1 3', 'badValue an 1 99'])),
    S('AK5', 1, 1, ['status* id 1 1 =A|E|M|P|R|W|X', 'error1 id 1 3', 'error2 id 1 3', 'error3 id 1 3', 'error4 id 1 3', 'error5 id 1 3'])),
  S('AK9', 1, 1, ['status* id 1 1 =A|E|M|P|R', 'included* n 1 6', 'received* n 1 6', 'accepted* n 1 6', 'error1 id 1 3', 'error2 id 1 3', 'error3 id 1 3', 'error4 id 1 3', 'error5 id 1 3']),
]);

// ---------------------------------------------------------------- EDIFACT D96A building blocks
const BGM = () => S('BGM', 1, 1, [['documentName', 'code an 1 3', 'codeList an 1 3', 'agency an 1 3', 'name an 1 35'], 'documentNumber an 1 35', 'messageFunction an 1 3', 'responseType an 1 3']);
const eDTM = (min = 0, max = 35) => S('DTM', min, max, [['period*', 'qualifier* an 1 3', 'value an 1 35', 'format an 1 3']]);
const FTX = (max = 99) => S('FTX', 0, max, ['subject* an 1 3', 'function an 1 3', ['reference', 'code an 1 17', 'codeList an 1 3', 'agency an 1 3'], ['text', 'line1 an 1 70', 'line2 an 1 70', 'line3 an 1 70', 'line4 an 1 70', 'line5 an 1 70'], 'language an 1 3']);
const RFF = (max = 1) => S('RFF', max === 1 ? 1 : 0, max, [['reference*', 'qualifier* an 1 3', 'number an 1 35', 'lineNumber an 1 6', 'version an 1 35']]);
const RFFLoop = (max = 10) => L('RFFLoop', 0, max, RFF(), eDTM(0, 5));
const NADLoop = () => L('NADLoop', 0, 99,
  S('NAD', 1, 1, ['partyQualifier* an 1 3', ['partyId', 'id an 1 35', 'codeList an 1 3', 'agency an 1 3'],
    ['nameAddress', 'line1 an 1 35', 'line2 an 1 35', 'line3 an 1 35', 'line4 an 1 35', 'line5 an 1 35'],
    ['partyName', 'name1 an 1 35', 'name2 an 1 35', 'name3 an 1 35', 'name4 an 1 35', 'name5 an 1 35', 'format an 1 3'],
    ['street', 'line1 an 1 35', 'line2 an 1 35', 'line3 an 1 35', 'line4 an 1 35'],
    'city an 1 35', 'countrySubEntity an 1 9', 'postalCode an 1 9', 'country an 1 3']),
  L('RFFLoop', 0, 10, RFF(), eDTM(0, 5)),
  L('CTALoop', 0, 5, S('CTA', 1, 1, ['function an 1 3', ['contact', 'id an 1 17', 'name an 1 35']]),
    S('COM', 0, 5, [['communication*', 'number* an 1 512', 'qualifier* an 1 3']])));
const CUXLoop = () => L('CUXLoop', 0, 5, S('CUX', 1, 1, [['currency', 'qualifier* an 1 3', 'code an 1 3', 'type an 1 3', 'rateBase an 1 4']]));
const LIN = () => S('LIN', 1, 1, ['lineNumber an 1 6', 'actionCode an 1 3', ['item', 'id an 1 35', 'type an 1 3', 'codeList an 1 3', 'agency an 1 3']]);
const PIA = () => S('PIA', 0, 25, ['function* an 1 3', ['itemId*', 'id* an 1 35', 'type an 1 3', 'codeList an 1 3', 'agency an 1 3']]);
const IMD = () => S('IMD', 0, 99, ['format an 1 3', ['characteristic', 'code an 1 3', 'codeList an 1 3', 'agency an 1 3'],
  ['description', 'code an 1 17', 'codeList an 1 3', 'agency an 1 3', 'text1 an 1 35', 'text2 an 1 35', 'language an 1 3']]);
const QTY = (max = 99) => S('QTY', 0, max, [['quantity*', 'qualifier* an 1 3', 'value* n 1 15', 'unit an 1 3']]);
const MOA = (min = 0, max = 99) => S('MOA', min, max, [['amount*', 'qualifier* an 1 3', 'value r 1 18', 'currency an 1 3', 'currencyQualifier an 1 3']]);
const PRILoop = () => L('PRILoop', 0, 25, S('PRI', 1, 1, [['price', 'qualifier* an 1 3', 'amount r 1 15', 'type an 1 3', 'basis an 1 3', 'basisQuantity n 1 9', 'unit an 1 3']]));
const TAXLoop = (max = 99) => L('TAXLoop', 0, max,
  S('TAX', 1, 1, ['function* an 1 3', ['type', 'code an 1 3', 'codeList an 1 3', 'agency an 1 3', 'description an 1 35'], ['account', 'code* an 1 6', 'codeList an 1 3', 'agency an 1 3'], 'assessmentBasis an 1 15',
    ['detail', 'rateId an 1 7', 'codeList an 1 3', 'agency an 1 3', 'rate an 1 17'], 'category an 1 3']),
  MOA(0, 1));
const UNS = () => S('UNS', 1, 1, ['sectionId* an 1 1 =D|S']);
const CNT = (max = 10) => S('CNT', 0, max, [['control*', 'qualifier* an 1 3', 'value* n 1 18', 'unit an 1 3']]);

const edifact = (message, title, segments) => ({ name: `edifact-${message.toLowerCase()}`, standard: 'edifact', message, version: 'D96A', title, segments });

const orders = edifact('ORDERS', 'Purchase order', [
  BGM(), eDTM(1, 35), FTX(), RFFLoop(), NADLoop(), CUXLoop(),
  L('LINLoop', 0, 200000, LIN(), PIA(), IMD(), QTY(), eDTM(0, 35), FTX(), MOA(0, 10), PRILoop(), RFFLoop()),
  UNS(), MOA(0, 12), CNT(),
]);

const invoic = edifact('INVOIC', 'Invoice', [
  BGM(), eDTM(1, 35), FTX(), RFFLoop(99), NADLoop(), CUXLoop(),
  L('PATLoop', 0, 10, S('PAT', 1, 1, ['type* an 1 3', ['terms', 'id an 1 17', 'codeList an 1 3', 'agency an 1 3', 'description1 an 1 35', 'description2 an 1 35'],
    ['period', 'reference* an 1 3', 'relation an 1 3', 'unit an 1 3', 'count n 1 3']]), eDTM(0, 5)),
  L('LINLoop', 0, 9999999, LIN(), PIA(), IMD(), QTY(), eDTM(0, 35), FTX(), MOA(0, 25), PRILoop(), RFFLoop(), TAXLoop()),
  UNS(), CNT(), MOA(1, 100), TAXLoop(),
]);

const desadv = edifact('DESADV', 'Despatch advice', [
  BGM(), eDTM(1, 10), FTX(), RFFLoop(), NADLoop(),
  L('TDTLoop', 0, 10, S('TDT', 1, 1, ['stageQualifier* an 1 3', 'journeyId an 1 17', ['mode', 'code an 1 3', 'name an 1 17'], ['means', 'code an 1 8', 'name an 1 17'],
    ['carrier', 'id an 1 17', 'codeList an 1 3', 'agency an 1 3', 'name an 1 35']])),
  L('CPSLoop', 1, 9999,
    S('CPS', 1, 1, ['hierarchicalId* an 1 12', 'parentId an 1 12', 'packagingLevel an 1 3']),
    L('PACLoop', 0, 999, S('PAC', 1, 1, ['numberOfPackages n 1 8', ['details', 'level an 1 3', 'related an 1 3', 'terms an 1 3'], ['type', 'code an 1 17', 'codeList an 1 3', 'agency an 1 3', 'name an 1 35']])),
    L('LINLoop', 0, 9999, LIN(), PIA(), IMD(), QTY(10), eDTM(0, 5), RFF(99))),
  CNT(5),
]);

for (const guide of [x850, x810, x856, x997, orders, invoic, desadv]) {
  fs.writeFileSync(path.join(__dirname, `${guide.name}.json`), JSON.stringify(guide, null, 1) + '\n');
}
console.log('wrote 7 guides');
