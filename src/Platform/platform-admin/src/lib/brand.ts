/**
 * Every product name the Studio shows, in one place.
 *
 * The name is not final, so nothing else writes it literally: a rename is one env var at build time
 * (VITE_PRODUCT_NAME / VITE_PRODUCT_SHORT), the same way the website reads site.name.
 */
const env = import.meta.env

/** The product — also the name of the pipeline engine (the DataFlow service). */
export const productName: string = env.VITE_PRODUCT_NAME || 'SpanFlow'

/** The short prefix the companion apps are named with. */
export const productShort: string = env.VITE_PRODUCT_SHORT || 'SF'

/** The website's slogan, shown under the name on the sign-in screen (website/src/site.config.ts `slogan`). */
export const productSlogan = 'Connect everything you already run.'

/** This admin app, and the Platform service behind it. */
export const studioName = `${productShort}Studio`

/** The mapping designer (the UFMapping service). */
export const mappingName = `${productShort} Mapping`
