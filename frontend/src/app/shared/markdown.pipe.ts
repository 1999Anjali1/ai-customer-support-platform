import { Pipe, PipeTransform } from '@angular/core';
import { marked } from 'marked';

marked.setOptions({ gfm: true, breaks: true });

/**
 * Renders AI replies (which may contain lists or **bold**) as HTML.
 * The output is bound with [innerHTML], which Angular sanitises, so scripts can't run.
 */
@Pipe({ name: 'markdown' })
export class MarkdownPipe implements PipeTransform {
  transform(value: string | null | undefined): string {
    if (!value) return '';
    return marked.parse(value, { async: false }) as string;
  }
}
