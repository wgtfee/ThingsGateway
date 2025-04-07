
export function getShowType() {
    return JSON.parse(localStorage.getItem('showType'))??0;
}
export function saveShowType(showType) {
    if (localStorage) {
        localStorage.setItem('showType', JSON.stringify(showType));
    }
}
