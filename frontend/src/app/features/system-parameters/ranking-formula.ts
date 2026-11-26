/**
 * Client-side mirror of the backend `RankingFormulaEvaluator` grammar (GB-910). It parses the
 * Effective Score formula so the studio can read it back as one plain-language sentence under the
 * code editor. The backend stays the single source of truth for validation and evaluation.
 */

export type ArithOp = '+' | '-' | '*' | '/';
export type CompareOp = '>' | '>=' | '<' | '<=' | '==' | '!=';
export type LogicOp = 'AND' | 'OR';
export type FormulaFunction = 'IF' | 'MIN' | 'MAX' | 'ROUND' | 'ABS' | 'CLAMP';

export type FormulaNode =
  | { kind: 'num'; value: number }
  | { kind: 'var'; name: string }
  | { kind: 'arith'; op: ArithOp; left: FormulaNode; right: FormulaNode }
  | { kind: 'cmp'; op: CompareOp; left: FormulaNode; right: FormulaNode }
  | { kind: 'logic'; op: LogicOp; left: FormulaNode; right: FormulaNode }
  | { kind: 'fn'; name: FormulaFunction; args: FormulaNode[] };

const COMPARE_OPS: readonly CompareOp[] = ['>', '>=', '<', '<=', '==', '!='];

const FUNCTION_NAMES: readonly FormulaFunction[] = ['IF', 'MIN', 'MAX', 'ROUND', 'ABS', 'CLAMP'];

/** Words used to read a formula aloud; supplied from the localization dictionary. */
export interface FormulaExplainWords {
  arith: Record<string, string>;
  compare: Record<string, string>;
  logic: Record<string, string>;
  explain: {
    take: string;
    ifText: string;
    thenStep: string;
    resultOf: string;
    min: string;
    max: string;
    minCap: string;
    maxFloor: string;
    round: string;
    roundInt: string;
    abs: string;
    clamp: string;
    listJoin: string;
    decimal: string;
  };
}

// ---------------------------------------------------------------------------
// Parsing
// ---------------------------------------------------------------------------

type Token =
  | { kind: 'num'; value: number }
  | { kind: 'id'; text: string }
  | { kind: 'sym'; text: string }
  | { kind: 'end' };

class FormulaSyntaxError extends Error {}

const TWO_CHAR_SYMBOLS: Record<string, string> = {
  '>=': '>=',
  '<=': '<=',
  '==': '==',
  '!=': '!=',
  '<>': '!=',
  '&&': 'AND',
  '||': 'OR',
};
const ONE_CHAR_SYMBOLS: Record<string, string> = {
  '+': '+',
  '-': '-',
  '*': '*',
  '/': '/',
  '(': '(',
  ')': ')',
  ',': ',',
  '>': '>',
  '<': '<',
  '=': '==',
};

const tokenize = (input: string): Token[] => {
  const tokens: Token[] = [];
  let i = 0;
  while (i < input.length) {
    const rest = input.slice(i);
    const space = /^\s+/.exec(rest);
    if (space) {
      i += space[0].length;
      continue;
    }
    const num = /^(\d+\.?\d*|\.\d+)/.exec(rest);
    if (num) {
      tokens.push({ kind: 'num', value: Number(num[0]) });
      i += num[0].length;
      continue;
    }
    const ident = /^[\p{L}_][\p{L}\p{N}_]*/u.exec(rest);
    if (ident) {
      const upper = ident[0].toUpperCase();
      tokens.push(
        upper === 'AND' || upper === 'OR'
          ? { kind: 'sym', text: upper }
          : { kind: 'id', text: ident[0] },
      );
      i += ident[0].length;
      continue;
    }
    const two = TWO_CHAR_SYMBOLS[rest.slice(0, 2)];
    if (two) {
      tokens.push({ kind: 'sym', text: two });
      i += 2;
      continue;
    }
    const one = ONE_CHAR_SYMBOLS[rest[0]];
    if (!one) throw new FormulaSyntaxError(`Unexpected character '${rest[0]}'`);
    tokens.push({ kind: 'sym', text: one });
    i += 1;
  }
  tokens.push({ kind: 'end' });
  return tokens;
};

class FormulaParser {
  private pos = 0;

  constructor(
    private readonly tokens: Token[],
    private readonly canonicalVariables: Map<string, string>,
  ) {}

  parseRoot(): FormulaNode {
    const node = this.parseOr();
    if (this.current.kind !== 'end') throw new FormulaSyntaxError('Trailing tokens');
    return node;
  }

  private get current(): Token {
    return this.tokens[Math.min(this.pos, this.tokens.length - 1)];
  }

  private isSym(...texts: string[]): boolean {
    const tok = this.current;
    return tok.kind === 'sym' && texts.includes(tok.text);
  }

  private consumeSym(): string {
    const tok = this.current;
    if (tok.kind !== 'sym') throw new FormulaSyntaxError('Expected symbol');
    this.pos++;
    return tok.text;
  }

  private parseOr(): FormulaNode {
    let left = this.parseAnd();
    while (this.isSym('OR')) {
      this.consumeSym();
      left = { kind: 'logic', op: 'OR', left, right: this.parseAnd() };
    }
    return left;
  }

  private parseAnd(): FormulaNode {
    let left = this.parseComparison();
    while (this.isSym('AND')) {
      this.consumeSym();
      left = { kind: 'logic', op: 'AND', left, right: this.parseComparison() };
    }
    return left;
  }

  private parseComparison(): FormulaNode {
    const left = this.parseAdditive();
    if (this.isSym(...COMPARE_OPS)) {
      const op = this.consumeSym() as CompareOp;
      return { kind: 'cmp', op, left, right: this.parseAdditive() };
    }
    return left;
  }

  private parseAdditive(): FormulaNode {
    let left = this.parseMultiplicative();
    while (this.isSym('+', '-')) {
      const op = this.consumeSym() as ArithOp;
      left = { kind: 'arith', op, left, right: this.parseMultiplicative() };
    }
    return left;
  }

  private parseMultiplicative(): FormulaNode {
    let left = this.parseUnary();
    while (this.isSym('*', '/')) {
      const op = this.consumeSym() as ArithOp;
      left = { kind: 'arith', op, left, right: this.parseUnary() };
    }
    return left;
  }

  private parseUnary(): FormulaNode {
    if (this.isSym('+')) {
      this.consumeSym();
      return this.parseUnary();
    }
    if (this.isSym('-')) {
      this.consumeSym();
      const inner = this.parseUnary();
      return inner.kind === 'num'
        ? { kind: 'num', value: -inner.value }
        : { kind: 'arith', op: '-', left: { kind: 'num', value: 0 }, right: inner };
    }
    return this.parsePrimary();
  }

  private parsePrimary(): FormulaNode {
    const tok = this.current;
    if (tok.kind === 'num') {
      this.pos++;
      return { kind: 'num', value: tok.value };
    }
    if (tok.kind === 'sym' && tok.text === '(') {
      this.pos++;
      const inner = this.parseOr();
      if (!this.isSym(')')) throw new FormulaSyntaxError('Missing )');
      this.pos++;
      return inner;
    }
    if (tok.kind === 'id') {
      this.pos++;
      if (this.isSym('(')) return this.parseFunction(tok.text);
      const name = this.canonicalVariables.get(tok.text.toLowerCase()) ?? tok.text;
      return { kind: 'var', name };
    }
    throw new FormulaSyntaxError('Unexpected token');
  }

  private parseFunction(rawName: string): FormulaNode {
    const name = rawName.toUpperCase() as FormulaFunction;
    if (!FUNCTION_NAMES.includes(name)) throw new FormulaSyntaxError(`Unknown function ${rawName}`);
    this.pos++; // '('
    const args: FormulaNode[] = [];
    if (!this.isSym(')')) {
      args.push(this.parseOr());
      while (this.isSym(',')) {
        this.pos++;
        args.push(this.parseOr());
      }
    }
    if (!this.isSym(')')) throw new FormulaSyntaxError('Missing ) after arguments');
    this.pos++;
    if (!hasValidArity(name, args.length)) throw new FormulaSyntaxError(`Bad arity for ${name}`);
    return { kind: 'fn', name, args };
  }
}

const hasValidArity = (name: FormulaFunction, count: number): boolean => {
  switch (name) {
    case 'IF':
    case 'CLAMP':
      return count === 3;
    case 'ABS':
      return count === 1;
    case 'ROUND':
      return count === 1 || count === 2;
    default:
      return count >= 2;
  }
};

/** Parses a formula string into a block tree, or returns `null` when it cannot be shown as blocks. */
export const parseFormula = (
  formula: string,
  knownVariables: readonly string[] = [],
): FormulaNode | null => {
  if (!formula.trim()) return null;
  const canonical = new Map(knownVariables.map((name) => [name.toLowerCase(), name]));
  try {
    return new FormulaParser(tokenize(formula), canonical).parseRoot();
  } catch {
    return null;
  }
};

// ---------------------------------------------------------------------------
// Helpers for the explanation
// ---------------------------------------------------------------------------

const precedenceOf = (node: FormulaNode): number => {
  switch (node.kind) {
    case 'logic':
      return node.op === 'OR' ? 1 : 2;
    case 'cmp':
      return 3;
    case 'arith':
      return node.op === '+' || node.op === '-' ? 4 : 5;
    default:
      return 6;
  }
};

const formatNumberToken = (value: number): string => {
  const text = String(Number(value.toFixed(6)));
  return text.includes('e') ? value.toFixed(6) : text;
};

// ---------------------------------------------------------------------------
// Plain-language explanation
// ---------------------------------------------------------------------------

const fill = (template: string, params: Record<string, string>): string =>
  template.replace(/\{(\w+)\}/g, (_, key: string) => params[key] ?? '');

const lowerFirst = (text: string): string =>
  text.length > 1 && text[1] === text[1].toLocaleUpperCase() && /\p{L}/u.test(text[1])
    ? text
    : text.charAt(0).toLocaleLowerCase() + text.slice(1);

export const joinList = (items: string[], lastWord: string): string =>
  items.length <= 1
    ? (items[0] ?? '')
    : `${items.slice(0, -1).join(', ')} ${lastWord} ${items[items.length - 1]}`;

interface FormulaDescriber {
  /** A phrase for one part of the formula, e.g. "tổng điểm nhân 0,8". */
  describe: (node: FormulaNode) => string;
  /** A full sentence for a value, e.g. "Lấy tổng điểm nhân 0,8." */
  sentence: (node: FormulaNode) => string;
  formatNumber: (value: number) => string;
}

const capitalize = (text: string): string =>
  `${text.charAt(0).toLocaleUpperCase()}${text.slice(1)}`;

const createFormulaDescriber = (
  words: FormulaExplainWords,
  variableLabel: (name: string) => string,
): FormulaDescriber => {
  const ex = words.explain;
  const formatNumber = (value: number): string => formatNumberToken(value).replace('.', ex.decimal);

  const describe = (node: FormulaNode): string => {
    switch (node.kind) {
      case 'num':
        return formatNumber(node.value);
      case 'var':
        return lowerFirst(variableLabel(node.name));
      case 'cmp':
        return `${describe(node.left)} ${words.compare[node.op]} ${describe(node.right)}`;
      case 'logic':
        return `${describe(node.left)} ${words.logic[node.op]} ${describe(node.right)}`;
      case 'arith': {
        const prec = precedenceOf(node);
        const opWord = words.arith[node.op];
        const left = describe(node.left);
        // A nested calculation on the right is read as "the result of …" so the order stays clear.
        const right =
          node.right.kind === 'arith'
            ? fill(ex.resultOf, { value: describe(node.right) })
            : describe(node.right);
        // A different kind of calculation on the left is read step by step: "…, then …".
        if (node.left.kind === 'arith' && precedenceOf(node.left) !== prec) {
          return fill(ex.thenStep, { left, op: opWord, right });
        }
        return `${left} ${opWord} ${right}`;
      }
      case 'fn':
        return describeFunction(node.name, node.args);
    }
  };

  // A phrase that already opens with "nếu …" must not be prefixed with "lấy".
  const opensWithIf = (node: FormulaNode): boolean => {
    if (node.kind !== 'fn') return false;
    if (node.name === 'IF') return true;
    const wrapsFirstArg =
      node.name === 'CLAMP' ||
      node.name === 'ROUND' ||
      ((node.name === 'MIN' || node.name === 'MAX') && node.args.length === 2);
    return wrapsFirstArg && opensWithIf(node.args[0]);
  };

  const take = (node: FormulaNode): string =>
    opensWithIf(node) ? describe(node) : fill(ex.take, { value: describe(node) });

  const describeFunction = (name: FormulaFunction, args: FormulaNode[]): string => {
    const [first, second, third] = args;
    switch (name) {
      case 'IF':
        return fill(ex.ifText, { cond: describe(first), then: take(second), else: take(third) });
      case 'MIN':
        return args.length === 2
          ? fill(ex.minCap, { value: describe(first), limit: describe(second) })
          : fill(ex.min, { list: joinList(args.map(describe), ex.listJoin) });
      case 'MAX':
        return args.length === 2
          ? fill(ex.maxFloor, { value: describe(first), limit: describe(second) })
          : fill(ex.max, { list: joinList(args.map(describe), ex.listJoin) });
      case 'ROUND':
        return second
          ? fill(ex.round, { value: describe(first), digits: describe(second) })
          : fill(ex.roundInt, { value: describe(first) });
      case 'ABS':
        return fill(ex.abs, { value: describe(first) });
      case 'CLAMP':
        return fill(ex.clamp, {
          value: describe(first),
          min: describe(second),
          max: describe(third),
        });
    }
  };

  return { describe, formatNumber, sentence: (node) => `${capitalize(take(node))}.` };
};

/** Reads the whole formula as one plain-language sentence, e.g. "Nếu … thì lấy …, ngược lại lấy …". */
export const describeFormula = (
  root: FormulaNode,
  words: FormulaExplainWords,
  variableLabel: (name: string) => string,
): string => createFormulaDescriber(words, variableLabel).sentence(root);
