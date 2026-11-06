/** An `.svg` import is its markup as text (angular.json `loader`, D65): only imported icons ship. */
declare module '*.svg' {
  const markup: string;
  export default markup;
}
