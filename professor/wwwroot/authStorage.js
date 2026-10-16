const tokenKey = "professor.authToken";
const userNameKey = "professor.userName";

export function getToken() {
    return sessionStorage.getItem(tokenKey);
}

export function getUserName() {
    return sessionStorage.getItem(userNameKey);
}

export function setToken(token, userName) {
    sessionStorage.setItem(tokenKey, token);
    sessionStorage.setItem(userNameKey, userName);
}

export function clearToken() {
    sessionStorage.removeItem(tokenKey);
    sessionStorage.removeItem(userNameKey);
}
