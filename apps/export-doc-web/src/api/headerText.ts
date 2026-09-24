/** Keep Unicode filenames/passwords out of the browser Headers ByteString boundary. */
export function encodeHeaderText(value: string) {
  return `UTF-8''${encodeURIComponent(value)}`;
}
