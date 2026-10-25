// SASISHO15 (P2): access token inakaa KWENYE KUMBUKUMBU tu (si localStorage) — XSS haiwezi kuisoma baadaye kutoka
// kwenye hifadhi ya browser, na inaisha baada ya dakika chache. Kikao kinaendelezwa na refresh token iliyo kwenye
// cookie ya HttpOnly (JavaScript haiioni kabisa).
let accessToken: string | null = null;
let onEnded: (() => void) | null = null;

export const getAccessToken = () => accessToken;
export const setAccessToken = (token: string | null) => {
  accessToken = token;
};

/** AuthContext inasajili hapa: kikao kikiisha (refresh imekataliwa) mtumiaji anarudishwa kwenye login. */
export const setSessionEndedHandler = (fn: (() => void) | null) => {
  onEnded = fn;
};
export const endSession = () => {
  accessToken = null;
  onEnded?.();
};
