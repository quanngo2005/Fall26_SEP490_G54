import { format, doc } from 'prettier';
import * as htmlPlugin from 'prettier/plugins/html';

export const parsers = {
  angular: { ...htmlPlugin.parsers.angular, astFormat: 'angular-compact' },
};

// Apply the standard Angular formatter, then remove only interpolation padding.
// A distinct AST format prevents Prettier from choosing its built-in HTML printer.
export const printers = {
  'angular-compact': {
    ...htmlPlugin.printers.html,
    embed(path, options) {
      if (path.node.kind !== 'root') return undefined;
      return async () => {
        const formatted = await format(options.originalText, {
          ...options,
          parser: 'angular',
          plugins: [htmlPlugin],
        });
        const compact = formatted.replace(
          /\{\{\s*([\s\S]*?)\s*\}\}/g,
          (_, expression) => `{{${expression.trim()}}}`,
        );
        return [compact.trimEnd(), doc.builders.hardline];
      };
    },
  },
};
